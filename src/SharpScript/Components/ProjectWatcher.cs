using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Rhino;
using SharpScript.Scripting;

namespace SharpScript.Components
{
    /// <summary>
    /// Keeps the component in step with the folder its project is mirrored into, so editing the
    /// same project in an IDE has the same effect as editing it in the editor here. It also owns
    /// the writing, because only something that knows when we last wrote can tell our own changes
    /// from somebody else's.
    /// </summary>
    internal sealed class ProjectWatcher : IDisposable
    {
        static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(250);

        readonly ScriptProject _project;
        readonly Action _pulled;

        FileSystemWatcher _watcher;
        CancellationTokenSource _pending;
        DateTime _mirrored = DateTime.MinValue;

        /// <param name="pulled">Raised on the UI thread once the project has read new sources in.</param>
        public ProjectWatcher(ScriptProject project, Action pulled)
        {
            _project = project;
            _pulled = pulled;
        }

        /// <summary>Writes the project out and notes when, which is what the guard below reads.</summary>
        public void Mirror()
        {
            _project.MirrorToDisk();
            _mirrored = DateTime.UtcNow;
        }

        /// <summary>Starts following the folder. Does nothing until the folder is actually there.</summary>
        public void Start()
        {
            if (_watcher != null || !Directory.Exists(_project.WorkingFolder)) return;

            _watcher = new FileSystemWatcher(_project.WorkingFolder)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            _watcher.Changed += OnChanged;
            _watcher.Created += OnChanged;
            _watcher.Deleted += OnChanged;
            _watcher.Renamed += OnChanged;
        }

        void OnChanged(object sender, FileSystemEventArgs e)
        {
            // An editor writing a file raises several events; only the last one is worth reading.
            _pending?.Cancel();
            _pending = new CancellationTokenSource();

            var token = _pending.Token;
            var seen = DateTime.UtcNow;

            Task.Delay(Settle, token).ContinueWith(_ =>
            {
                if (token.IsCancellationRequested) return;

                RhinoApp.InvokeOnUiThread(new Action(() => Pull(seen)));
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
        }

        void Pull(DateTime seen)
        {
            // Mirroring the project trips the watcher, and pulling the result back in would reload
            // the files, rebuild the language service and redraw for no change at all. An edit made
            // elsewhere arrives after the write and still gets through.
            if (seen <= _mirrored) return;

            if (!_project.PullFromDisk()) return;

            _pulled();
        }

        public void Dispose()
        {
            _pending?.Cancel();

            _watcher?.Dispose();
            _watcher = null;
        }
    }
}
