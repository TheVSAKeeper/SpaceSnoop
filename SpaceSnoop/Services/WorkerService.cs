﻿using System.ComponentModel;
using System.Diagnostics;

namespace SpaceSnoop.Services;

public class WorkerService : IDisposable
{
    private readonly BackgroundWorker _backgroundWorker;
    private readonly IDiskSpaceCalculator _diskSpaceCalculator;
    private readonly ILogger<WorkerService> _logger;

    public WorkerService(IDiskSpaceCalculator diskSpaceCalculator, BackgroundWorker backgroundWorker, ILogger<WorkerService> logger)
    {
        _diskSpaceCalculator = diskSpaceCalculator;
        _backgroundWorker = backgroundWorker;
        _logger = logger;

        Initialize();
    }

    public void Dispose()
    {
        _backgroundWorker.DoWork -= OnDoWork;
        _backgroundWorker.RunWorkerCompleted -= OnRunWorkerCompleted;

        _backgroundWorker.Dispose();

        GC.SuppressFinalize(this);
    }

    public event EventHandler<DirectorySpace>? WorkCompleted;

    private void Initialize()
    {
        _backgroundWorker.DoWork += OnDoWork;
        _backgroundWorker.RunWorkerCompleted += OnRunWorkerCompleted;
        _backgroundWorker.WorkerSupportsCancellation = true;
    }

    public void StartWorker(string disk, CancellationToken cancellationToken)
    {
        WorkerRequest workerRequest = new(disk, cancellationToken);
        _backgroundWorker.RunWorkerAsync(workerRequest);
    }

    private void OnDoWork(object? sender, DoWorkEventArgs args)
    {
        if (args.Argument is not WorkerRequest(var disk, var cancellationToken) || string.IsNullOrWhiteSpace(disk))
        {
            return;
        }

        DirectoryInfo directory = new(disk);

        if (!directory.Exists)
        {
            _logger.LogError("Расчет для каталога {Directory} невозможен. Директория не найдена.", directory.FullName);
            return;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        DirectoryInfo directoryInfo = new(disk);

        try
        {
            DirectorySpace directorySpace = _diskSpaceCalculator.Calculate(directoryInfo, cancellationToken);
            args.Result = directorySpace;
        }
        catch (OperationCanceledException)
        {
            args.Cancel = true;
        }
        finally
        {
            if (args.Cancel == false)
            {
                _logger.LogInformation("Расчет для каталога {Directory} завершен за {ElapsedSeconds:F2} с ({ElapsedMilliseconds} мс).",
                    directory.FullName, stopwatch.Elapsed.TotalSeconds, stopwatch.ElapsedMilliseconds);
            }

            stopwatch.Stop();
        }
    }

    private void OnRunWorkerCompleted(object? sender, RunWorkerCompletedEventArgs args)
    {
        if (args.Cancelled)
        {
            _logger.LogInformation("Сканирование было отменено пользователем.");
        }
        else if (args.Error != null)
        {
            _logger.LogError(args.Error, "Произошла ошибка во время сканирования.");
        }
        else if (args.Result is DirectorySpace data)
        {
            WorkCompleted?.Invoke(this, data);
        }
    }

    private record WorkerRequest(string Disk, CancellationToken CancellationToken);
}
