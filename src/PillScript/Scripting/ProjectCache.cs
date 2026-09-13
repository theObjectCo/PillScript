using System;
using System.IO;
using System.Threading.Tasks;

namespace PillScript.Scripting
{
    /// <summary>
    /// Where the working folders are kept, and when they are deleted.
    ///
    /// A working folder is a mirror. The files themselves are stored in the Grasshopper document,
    /// so the only thing a folder holds that is not elsewhere is the restore. A folder nothing has
    /// compiled from in a week belongs to a definition that has moved on, and the next compile
    /// writes it out again. The sweep runs once, on its own thread, as Grasshopper loads.
    /// </summary>
    internal static class ProjectCache
    {
        static readonly TimeSpan Stale = TimeSpan.FromDays(7);

        public static string Root => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PillScript", "projects");

        public static string FolderFor(Guid id) => Path.Combine(Root, id.ToString("N"));

        /// <summary>
        /// Sweeps in the background and reports what it deleted. Walking a folder tree during the
        /// Grasshopper load would delay it, and a failed sweep must not fail the load.
        /// </summary>
        public static void SweepLater()
        {
            Task.Run(() =>
            {
                try
                {
                    var removed = Sweep();
                    if (removed == 0) return;

                    Rhino.RhinoApp.InvokeOnUiThread(new Action(() => Rhino.RhinoApp.WriteLine(
                        "PillScript: removed " + removed + " script folder(s) untouched for "
                        + Stale.Days + " days. The scripts themselves are kept in their .gh files.")));
                }
                catch (Exception exception)
                {
                    Rhino.RhinoApp.WriteLine("PillScript: the old script folders could not be "
                                             + "swept (" + exception.Message + ").");
                }
            });
        }

        public static int Sweep()
        {
            if (!Directory.Exists(Root)) return 0;

            var removed = 0;
            var cutoff = DateTime.UtcNow - Stale;

            foreach (var folder in Directory.EnumerateDirectories(Root))
            {
                if (Touched(folder) > cutoff) continue;

                try
                {
                    Directory.Delete(folder, true);
                    removed++;
                }
                catch (IOException)
                {
                    // Open in an IDE, or another Rhino is using it. The next sweep can take it.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return removed;
        }

        /// <summary>
        /// When the folder was last written to. The sources sit at the top level and mirroring
        /// rewrites them, so the newest file there shows whether the folder is still in use. The
        /// obj and bin folders underneath belong to the restore and are not examined.
        /// </summary>
        static DateTime Touched(string folder)
        {
            var newest = Directory.GetLastWriteTimeUtc(folder);

            try
            {
                foreach (var file in Directory.EnumerateFiles(folder))
                {
                    var at = File.GetLastWriteTimeUtc(file);
                    if (at > newest) newest = at;
                }
            }
            catch (IOException)
            {
                return DateTime.UtcNow;   // unreadable: leave it alone
            }

            return newest;
        }
    }
}
