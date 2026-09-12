using System;
using System.Drawing;
using System.IO;

namespace PillScript.Components
{
    /// <summary>
    /// The component's icon: the same capsule the editor wears as its logo, kept here as the
    /// rendered artwork rather than redrawn, so the two cannot drift apart. The drawing itself
    /// is the icPill symbol in Editor/web/index.html; if it changes there, render it again at
    /// 24 by 24 and replace the bytes below.
    ///
    /// Carried in the source rather than as a resource file, which keeps the plugin a single .gha.
    /// </summary>
    internal static class ComponentIcon
    {
        static Bitmap _bitmap;

        public static Bitmap Bitmap => _bitmap ??= Load();

        static Bitmap Load()
        {
            var bytes = Convert.FromBase64String(Artwork);

            // The stream has to outlive the bitmap, so the bitmap is copied off it and the
            // original let go. A Bitmap built straight from a stream keeps reading from it.
            using var stream = new MemoryStream(bytes);
            using var loaded = new Bitmap(stream);

            return new Bitmap(loaded);
        }

        /// <summary>A 24 by 24 PNG with transparency.</summary>
        const string Artwork =
            "iVBORw0KGgoAAAANSUhEUgAAABgAAAAYCAYAAADgdz34AAADBElEQVR4nLSV3U9ScRjHH85BEUVwIojzJQNEU9zQLmrWTbU1" +
            "N7fMmNOwbtzsZf0H5dq66K71clXWX1CuZpuW6TBFbbMyIBGnmKi8CUcN5UWFg/QD0h00ETS/27Ozc57n+Xx/O8/v/A4GRywM" +
            "jlj/w4CBgrZXkg6HUHNd3UMmDrdJ3/rmstPzpF2leoQeb1BrcDigWhSKwbN5vKbm3AxmkUGd6qLRz3FLZZV6w1QHSvsPZdAo" +
            "l08+vXS+QlYhA9LjBY9GDdlWG82KgTg19xgYLZYvqCwQqk14Bgp5veFV/UVJCl8AngkdOPp6welwgsm3BmyzBWOlsW+isuyt" +
            "+oQMQvC2hmoxnSsA98Q4OAaUsDimD8PNPgAHujKTsNDQ+QkbhOGN1WI8g4vgOiBUSiDUum24aQ2AKSyBVadzCJWju8jOisvg" +
            "yuV6Y0NZIYJnhVdODPbvgpflcMDOFcx9HBl5iVpsKIKh3n2HXCdvmhKfUoi0+lF/cN6Ap05ogRjVRsGl+RzoE8qINx86W1HL" +
            "MAr7Vj8eG351sqbluURaIoJJO+B9PR0emNYkBwJkFFxZKCPeRuBKFPNUBh4LXnurTcJjY6CfcYFuPgl+mOnJM7Zxl4+2znAj" +
            "eHk0vAfF7E4Ovjf8hSQrHQ/DVd8XQKuzQSAQACJIZxCrs65SThJDU3wyJvyfBo3XbgzXND+TbcGHRhdAo7OCz2WGNa8VwOeG" +
            "TX4pI0WYvvy+u+teLHhIUbuopvb6/czCM1U8Dn0brh4LwS0RuNsMnIITUFnwe7HrU9dd1NIbCx4S9bBj0FNZd9IEF2D81yoF" +
            "bo6CS1lfie7O9ta/cCPsI6qB37+xiSlHZtEOxsFhX9q1cgo85muhijqDII/PE9CD5GnrSgYtauW5xSBlf0sYvtMATHPTPyXC" +
            "rCqMtOe5Vrw0EsGLRCw4nmac6e58/QDieOcxDZDcxmn9UA6PySnI9IryBRhJuhb6B5TvHqPcZ9jxEcWjvX51OSjKIXLsmlCM" +
            "oViCA4gWZz4IB9R+BofWHwAAAP//fv7oZwAAAAZJREFUAwCONHbdGYGXMwAAAABJRU5ErkJggg==";
    }
}
