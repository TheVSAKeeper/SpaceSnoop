using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.Bootstrap;

public sealed class ToastNotifier
{
    private static readonly ConditionalWeakTable<ToastViewModel, ICommand> UndoCommands = [];

    private readonly ToastHostViewModel _toasts;
    private readonly ShellPreferences _prefs;
    private Action? _pendingUndo;
    private Action? _lastUndo;
    private ToastViewModel? _lastUndoToast;

    public bool CanUndo => _lastUndo is not null;

    public ToastNotifier(ToastHostViewModel toasts, ShellPreferences prefs)
    {
        _toasts = toasts;
        _prefs = prefs;
        _toasts.Toasts.CollectionChanged += OnToastsChanged;
    }

    public static ICommand? GetUndoCommand(ToastViewModel? toast)
    {
        return toast is not null && UndoCommands.TryGetValue(toast, out var command) ? command : null;
    }

    public void ClearUndo()
    {
        _lastUndo = null;
        _lastUndoToast = null;
    }

    public void Notify(string message, StatusSeverity severity = StatusSeverity.Info, Action? onUndo = null)
    {
        if (onUndo is not null && _lastUndoToast is { } previousToast)
        {
            _toasts.DismissCommand.Execute(previousToast);
        }

        _lastUndo = onUndo;
        _lastUndoToast = null;

        if (!_prefs.EnableToastNotifications)
        {
            return;
        }

        _pendingUndo = onUndo;
        try
        {
            var toast = _toasts.Show(message, severity);
            AttachUndo(toast, onUndo);
        }
        finally
        {
            _pendingUndo = null;
        }
    }

    public bool UndoLast()
    {
        for (var i = _toasts.Toasts.Count - 1; i >= 0; i--)
        {
            if (GetUndoCommand(_toasts.Toasts[i]) is { } command && command.CanExecute(null))
            {
                command.Execute(null);
                return true;
            }
        }

        if (_lastUndo is not { } undo)
        {
            return false;
        }

        ClearUndo();
        undo();
        return true;
    }

    private void OnToastsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add
            && _pendingUndo is { } onUndo
            && e.NewItems is { Count: > 0 } items
            && items[0] is ToastViewModel toast)
        {
            _pendingUndo = null;
            AttachUndo(toast, onUndo);
            return;
        }

        if (_lastUndoToast is not null && !_toasts.Toasts.Contains(_lastUndoToast))
        {
            ClearUndo();
        }
    }

    private void AttachUndo(ToastViewModel toast, Action? onUndo)
    {
        if (onUndo is null)
        {
            return;
        }

        _lastUndoToast = toast;

        if (UndoCommands.TryGetValue(toast, out _))
        {
            return;
        }

        var undone = false;
        UndoCommands.Add(
            toast,
            new RelayCommand(() =>
            {
                if (undone || !ReferenceEquals(_lastUndo, onUndo))
                {
                    return;
                }

                undone = true;
                ClearUndo();
                onUndo();
                _toasts.DismissCommand.Execute(toast);
            }));
    }
}
