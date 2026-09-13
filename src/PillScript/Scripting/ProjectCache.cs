using System;
using System.IO;
using System.Threading.Tasks;

namespace PillScript.Scripting
{
    /// <summary>
    /// Where the working folders live, and what becomes of the ones nobody comes back to.
    ///
    /// A working folder is a mirror: the files themselves are kept in the Grasshopper document, so
    /// a folder is worth no more than the restore sitting in it. One nobody has compiled from in a
    /// week is a cache for a definition that has moved on, and the next compile writes it back
    /// anyway. They are swept once, as Grasshopper loads, on a thread of their own.
    /// </summary>
    internal static class ProjectCache
    {
        static readonly TimeSpan Stale = TimeSpan.FromDays(7);

        public static string Root => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PillScript", "projects");

        public static string FolderFor(Guid id) => Path.Combine(Root, id.ToString("N"));

        /// <summary>
        /// Sweeps in the background and says so if anything went. Loading Grasshopper is not the
        /// moment to walk a folder tree, and a sweep that fails is not a reason to fail to load.
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
                    // Open in an IDE, or another Rhino is working in it. It can go next time.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return removed;
        }

        /// <summary>
        /// When the folder was last written to. The sources sit at the top level and mirroring
        /// rewrites them, so the newest file up there is what says whether anybody still uses it;
        /// obj and bin underneath are the restore's business and are not asked.
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
