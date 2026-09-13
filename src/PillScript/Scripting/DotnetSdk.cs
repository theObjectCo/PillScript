using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace PillScript.Scripting
{
    /// <summary>
    /// Runs the .NET SDK on the script's project. Restoring packages and building a referenced
    /// project both go through here, and this is the only place that knows where dotnet.exe may
    /// be.
    /// </summary>
    internal static class DotnetSdk
    {
        static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

        /// <summary>The SDK, or null when the machine has none. The caller decides what to say.</summary>
        public static string Find()
        {
            var configured = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (!string.IsNullOrEmpty(configured) && File.Exists(configured)) return configured;

            var candidates = new List<string>
            {
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "dotnet", "dotnet.exe")
            };

            var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

            candidates.AddRange(pathVariable.Split(Path.PathSeparator)
                .Where(folder => !string.IsNullOrWhiteSpace(folder))
                .Select(folder => Path.Combine(folder.Trim(), "dotnet.exe")));

            return candidates.FirstOrDefault(File.Exists);
        }

        /// <summary>Runs an SDK command and returns whether it succeeded, along with its log.</summary>
        public static bool Run(string dotnet, IReadOnlyList<string> arguments, string folder, out string output)
        {
            var startInfo = new ProcessStartInfo(dotnet)
            {
                WorkingDirectory = folder,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

            var log = new StringBuilder();

            try
            {
                using var process = new Process { StartInfo = startInfo };
                process.OutputDataReceived += (_, e) => { if (e.Data != null) log.AppendLine(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) log.AppendLine(e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!process.WaitForExit((int)Timeout.TotalMilliseconds))
                {
                    try { process.Kill(true); } catch (Exception) { }

                    output = "The command timed out.";
                    return false;
                }

                output = log.ToString();
                return process.ExitCode == 0;
            }
            catch (Exception exception)
            {
                output = exception.Message;
                return false;
            }
        }

        /// <summary>Keeps the last few lines of a tool log, where the error is usually reported.</summary>
        public static string Tail(string text, int lines = 6)
        {
            var all = Lines(text).ToList();
            return string.Join(" ", all.Skip(Math.Max(0, all.Count - lines)));
        }

        public static IEnumerable<string> Lines(string text)
            => (text ?? string.Empty)
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0);
    }
}
