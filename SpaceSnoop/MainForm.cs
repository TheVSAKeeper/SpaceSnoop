using System.Diagnostics;
using SpaceSnoop.Extensions;
using SpaceSnoop.Services;

namespace SpaceSnoop;

public partial class MainForm : Form
{
    private readonly IAdministratorChecker _administratorChecker;

    public MainForm(
        IAdministratorChecker administratorChecker,
        WorkerService workerService,
        ColorService colorService)
    {
        _administratorChecker = administratorChecker;
        _workerService = workerService;
        _colorService = colorService;

        InitializeComponent();

        _directoriesTreeView.ShowNodeToolTips = true;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData != Keys.Escape)
        {
            return base.ProcessCmdKey(ref msg, keyData);
        }

        StopWorker();
        return true;
    }

    private void OnFormLoaded(object sender, EventArgs args)
    {
        InitializeWorker();
        InitializeSorting();
        InitializeColorService();

        FillDrives();

        SetDefaultSettings();
    }

    protected override void OnFormClosing(FormClosingEventArgs args)
    {
        FinalizeWorker();
        FinalizeSorting();
        FinalizeColorService();

        base.OnFormClosing(args);
    }

    private void OnStartButtonClicked(object sender, EventArgs args)
    {
        string disk = _hardDiskComboBox.SelectedItem?.ToString() ?? _hardDiskComboBox.Text;

        if (string.IsNullOrWhiteSpace(disk))
        {
            return;
        }

        StartScanning(disk);
    }

    private void OnStopButtonClicked(object sender, EventArgs args)
    {
        StopWorker();
    }

    private void OnDirectoriesTreeViewBeforeExpanded(object sender, TreeViewCancelEventArgs args)
    {
        TreeNode? parent = args.Node;

        if (parent == null || _isSorting || parent.Nodes.Count <= 0)
        {
            return;
        }

        foreach (TreeNode node in parent.Nodes)
        {
            if (node.Tag is not DirectorySpace diskSpace)
            {
                continue;
            }

            if (node.Nodes.Count > 0)
            {
                break;
            }

            node.FillParentNode(diskSpace);
            _colorService.UpdateAssignedNodesColor(node);
        }
    }

    private void OnNodeMouseClicked(object sender, TreeNodeMouseClickEventArgs args)
    {
        if (args.Button != MouseButtons.Right)
        {
            return;
        }

        if (args.Node.Tag is SpaceBase selectedSpace)
        {
            Process.Start("explorer.exe", selectedSpace.Path);
        }
    }

    private void OnChooseDirectoryClicked(object sender, EventArgs e)
    {
        using FolderBrowserDialog folderBrowserDialog = new();

        DialogResult result = folderBrowserDialog.ShowDialog();

        if (result != DialogResult.OK || string.IsNullOrWhiteSpace(folderBrowserDialog.SelectedPath))
        {
            return;
        }

        string selectedPath = folderBrowserDialog.SelectedPath;

        int index = _hardDiskComboBox.Items.IndexOf(selectedPath);

        _hardDiskComboBox.SelectedIndex = index == -1
            ? _hardDiskComboBox.Items.Add(selectedPath)
            : index;

        StartScanning(selectedPath);
    }

    private void SetDefaultSettings()
    {
        Text = _administratorChecker.IsCurrentUserAdmin()
            ? "SpaceSnoop (Запущено от имени администратора)"
            : "SpaceSnoop";

        _hardDiskComboBox.SelectedIndex = 0;

        _useMultithreadingCheckBox.Checked = true;
    }

    private void FillDrives()
    {
        DriveInfo[] hardDisk = DriveInfo.GetDrives();

        foreach (DriveInfo disk in hardDisk)
        {
            _hardDiskComboBox.Items.Add(disk.Name);
        }
    }

    private void StartScanning(string disk)
    {
        StartProgressBar();
        RemovePathNode(disk);
        StartWorker(disk);
    }

    private void RemovePathNode(string path)
    {
        for (int i = 0; i < _directoriesTreeView.Nodes.Count; i++)
        {
            TreeNode node = _directoriesTreeView.Nodes[i];

            if (node.Tag is not SpaceBase space
                || space.Path.EndsWith(path, StringComparison.CurrentCultureIgnoreCase) == false)
            {
                continue;
            }

            _directoriesTreeView.Nodes.Remove(node);
            break;
        }
    }

    private void StartProgressBar()
    {
        _calculateProgressBar.Invoke(() => _calculateProgressBar.Style = ProgressBarStyle.Marquee);
    }

    private void StopProgressBar()
    {
        _calculateProgressBar.Invoke(() => _calculateProgressBar.Style = ProgressBarStyle.Blocks);
    }

    #region ColorService

    private readonly ColorService _colorService;

    private void InitializeColorService()
    {
        _colorService.Initialize(_intensityBar);
        _colorService.IntensityChanged += OnIntensityChanged;
    }

    private void FinalizeColorService()
    {
        _colorService.IntensityChanged -= OnIntensityChanged;
        _colorService.Dispose();
    }

    private void OnIntensityChanged(object? sender, int intensity)
    {
        _intensityGroupBox.Text = $"Интенсивность: {intensity}";
        _colorService.UpdateNodesColor(_directoriesTreeView.Nodes);
    }

    #endregion

    #region WorkerService

    private readonly WorkerService _workerService;
    private CancellationTokenSource? _cancellationTokenSource;

    private void InitializeWorker()
    {
        _workerService.WorkCompleted += OnWorkCompleted;
    }

    private void FinalizeWorker()
    {
        _workerService.WorkCompleted -= OnWorkCompleted;
    }

    private void OnWorkCompleted(object? sender, DirectorySpace directorySpace)
    {
        TreeNode addedParent = _directoriesTreeView.Nodes.AddSpaceNode(directorySpace).FillParentNode(directorySpace);
        _colorService.UpdateAssignedNodesColor(addedParent);
        SortNodes();
        StopProgressBar();
    }

    private void StartWorker(string disk)
    {
        _cancellationTokenSource = new CancellationTokenSource();
        _workerService.StartWorker(disk, _cancellationTokenSource.Token);
    }

    public void StopWorker()
    {
        _cancellationTokenSource?.Cancel();
    }

    #endregion
}
