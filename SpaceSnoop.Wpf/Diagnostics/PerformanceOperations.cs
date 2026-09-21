using KeepShell.Diagnostics;
using System.Windows.Threading;

namespace SpaceSnoop.Wpf.Diagnostics;

public sealed class PerformanceOperations(PerformanceMonitor monitor)
{
    private readonly Dispatcher _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

    private readonly Lock _gate = new();

    private PerformanceOperation? _current;

    private PerformanceOperation? _last;

    private IDisposable? _frames;

    public event EventHandler? Changed;

    public PerformanceOperation? Current => Volatile.Read(ref _current);

    public PerformanceOperation? Last => Volatile.Read(ref _last);

    public bool TryReport(PerformanceOperation operation, PerformanceOperation? own)
    {
        lock (_gate)
        {
            if (_current is not null && !ReferenceEquals(_current, own))
            {
                return false;
            }

            Volatile.Write(ref _current, operation);
            monitor.SetPhase(operation.Name);
            _frames ??= monitor.WatchFrames();

            return true;
        }
    }

    public void Release(PerformanceOperation? own)
    {
        lock (_gate)
        {
            if (own is null || !ReferenceEquals(_current, own))
            {
                return;
            }

            Volatile.Write(ref _current, null);
            monitor.SetPhase(null);

            _frames?.Dispose();
            _frames = null;
        }
    }

    public void ReportRun(PerformanceOperation run)
    {
        Volatile.Write(ref _last, run);
        Notify();
    }

    public void ClearRun()
    {
        if (Volatile.Read(ref _last) is null)
        {
            return;
        }

        Volatile.Write(ref _last, null);
        Notify();
    }

    private void Notify()
    {
        if (_dispatcher.CheckAccess())
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        _dispatcher.BeginInvoke(() => Changed?.Invoke(this, EventArgs.Empty));
    }
}
