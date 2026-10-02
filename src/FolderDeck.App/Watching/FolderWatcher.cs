using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Threading;
using FolderDeck.App.ViewModels;
using FolderDeck.Core.Watching;

namespace FolderDeck.App.Watching;














public sealed class FolderWatcher : IDisposable
{
    private readonly FolderPanelViewModel _panel;
    private readonly Dispatcher _dispatcher;
    private readonly ChangeCoalescer _coalescer;

    private FileSystemWatcher? _watcher;
    private bool _disposed;


    private bool _reviving;





    public FolderWatcher(
        FolderPanelViewModel panel, Dispatcher dispatcher, Func<bool> isOperationRunning)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(isOperationRunning);

        _panel = panel;
        _dispatcher = dispatcher;





        _coalescer = new ChangeCoalescer(suppressed: isOperationRunning);
        _coalescer.Due += OnDue;

        _panel.PropertyChanged += OnPanelPropertyChanged;
        _panel.LoadCompleted += OnPanelLoadCompleted;
        Retarget();
    }





    public int SkippedCount { get; private set; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _panel.PropertyChanged -= OnPanelPropertyChanged;
        _panel.LoadCompleted -= OnPanelLoadCompleted;
        _coalescer.Due -= OnDue;
        _coalescer.Dispose();
        Stop();
    }

    private void OnPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FolderPanelViewModel.CurrentPath))
        {
            Retarget();
        }
    }





    private void OnPanelLoadCompleted(object? sender, EventArgs e) => TryRevive();



















    private void TryRevive()
    {
        if (_disposed)
        {
            return;
        }


        if (_watcher is not null)
        {
            return;
        }


        if (_panel.CurrentPath is not { } path)
        {
            return;
        }

        if (_reviving)
        {
            return;
        }

        _reviving = true;
        _ = Task.Run(() => Revive(path));
    }





    private void Revive(string path)
    {
        FileSystemWatcher? made = null;

        try
        {
            made = new FileSystemWatcher(path);
            made.Created += OnChanged;
            made.Changed += OnChanged;
            made.Deleted += OnChanged;
            made.Renamed += OnRenamed;
            made.Error += OnError;
            made.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {

            if (made is not null)
            {
                Discard(made);
                made = null;
            }
        }

        if (_disposed || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
        {
            if (made is not null)
            {
                Discard(made);
            }

            return;
        }

        var revived = made;
        _dispatcher.BeginInvoke(() => FinishRevive(path, revived));
    }





    private void FinishRevive(string path, FileSystemWatcher? made)
    {
        _reviving = false;

        if (made is null)
        {

            SkippedCount++;
            return;
        }


        if (_disposed
            || _watcher is not null
            || _panel.CurrentPath is not { } current
            || !string.Equals(current, path, StringComparison.OrdinalIgnoreCase))
        {
            Discard(made);
            return;
        }


        _watcher = made;
    }





    private void Retarget()
    {
        _coalescer.Cancel();

        var path = _panel.CurrentPath;

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
        }

        if (path is null)
        {
            Stop();
            return;
        }

        try
        {
            if (_watcher is null)
            {
                _watcher = new FileSystemWatcher(path);
                _watcher.Created += OnChanged;
                _watcher.Changed += OnChanged;
                _watcher.Deleted += OnChanged;
                _watcher.Renamed += OnRenamed;
                _watcher.Error += OnError;
            }
            else
            {
                _watcher.Path = path;
            }

            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {

            SkippedCount++;
            Stop();
        }
    }

    private void Stop()
    {
        if (_watcher is null)
        {
            return;
        }

        Discard(_watcher);
        _watcher = null;
    }


    private void Discard(FileSystemWatcher watcher)
    {
        watcher.Created -= OnChanged;
        watcher.Changed -= OnChanged;
        watcher.Deleted -= OnChanged;
        watcher.Renamed -= OnRenamed;
        watcher.Error -= OnError;

        watcher.Dispose();
    }







    private void OnChanged(object sender, FileSystemEventArgs e) => _coalescer.Signal();

    private void OnRenamed(object sender, RenamedEventArgs e) => _coalescer.Signal();





    private void OnError(object sender, ErrorEventArgs e) => _coalescer.SignalNow();

    private void OnDue(object? sender, EventArgs e)
    {
        if (_disposed || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
        {
            return;
        }

        _dispatcher.BeginInvoke(Refresh);
    }


    private void Refresh()
    {
        if (_disposed)
        {
            return;
        }




        if (_panel.IsRecursiveSearch)
        {
            return;
        }



        _ = _panel.RefreshAsync();
    }
}
