using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpScript.Editor
{
    /// <summary>
    /// A PowerShell running in the script's project folder with its streams redirected, which is
    /// what the terminal pane talks to. It is a line shell rather than a terminal: there is no
    /// pseudo console behind it, so a full screen program like an editor or a pager will not draw
    /// properly. Running a build, adding a package or using git works, and that is what it is for.
    /// </summary>
    internal sealed class ShellSession : IDisposable
    {
        static readonly string NewLine = new string(new[] { (char)13, (char)10 });

        Process _process;
        bool _disposed;

        /// <summary>Raised with output as it arrives, already ending in a line break.</summary>
        public event Action<string> Output;

        /// <summary>Raised once the shell has exited.</summary>
        public event Action Exited;

        public bool Running => _process != null && !_process.HasExited;

        public void Start(string workingDirectory)
        {
            if (_process != null) return;

            var info = new ProcessStartInfo(Shell())
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            info.ArgumentList.Add("-NoLogo");
            info.ArgumentList.Add("-NoProfile");

            _process = new Process { StartInfo = info, EnableRaisingEvents = true };
            _process.Exited += (_, __) => Exited?.Invoke();

            _process.Start();

            // Read characters rather than lines. A prompt has no line break after it, so reading
            // by line leaves it sitting in the buffer until the next command finishes, and the
            // pane looks a step behind everything that is happening.
            Pump(_process.StandardOutput);
            Pump(_process.StandardError);

            Output?.Invoke(workingDirectory + NewLine);
        }

        void Pump(StreamReader reader)
        {
            Task.Run(async () =>
            {
                var buffer = new char[2048];

                while (true)
                {
                    int read;

                    try
                    {
                        read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        return;
                    }

                    if (read <= 0) return;

                    Output?.Invoke(new string(buffer, 0, read));
                }
            });
        }

        /// <summary>
        /// Sends a line to the shell. The pane edits the line and echoes it while it is being
        /// typed, so nothing is echoed here: a second copy would only duplicate what the shell
        /// prints back itself.
        /// </summary>
        public void Write(string text)
        {
            if (_process == null || string.IsNullOrEmpty(text)) return;

            try
            {
                _process.StandardInput.Write(text);
                _process.StandardInput.Flush();
            }
            catch (Exception)
            {
                // The shell has gone; the exit notice is already on its way.
            }
        }

        /// <summary>Accepted so the pane can report its size, though a line shell has no use for it.</summary>
        public void Resize(int columns, int rows) { }

        /// <summary>Prefers a real PowerShell 7 install, then the one in Windows.</summary>
        static string Shell()
        {
            foreach (var candidate in Candidates())
            {
                if (!File.Exists(candidate)) continue;

                // Entries under WindowsApps are Store execution aliases: empty reparse points that
                // start the app beside this process rather than under it.
                if (candidate.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (new FileInfo(candidate).Length == 0) continue;

                return candidate;
            }

            var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            return Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
        }

        static IEnumerable<string> Candidates()
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            yield return Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe");

            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

            foreach (var folder in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(folder)) continue;
                yield return Path.Combine(folder.Trim(), "pwsh.exe");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_process == null) return;

            try
            {
                if (!_process.HasExited) _process.Kill(true);
            }
            catch (Exception)
            {
                // Already gone.
            }

            _process.Dispose();
            _process = null;
        }
    }
}
