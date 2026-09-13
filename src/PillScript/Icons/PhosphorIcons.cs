using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace PillScript.Icons
{
    /// <summary>
    /// Icons by name from the Phosphor set: some nine thousand drawings under the MIT licence,
    /// too many to ship inside a plugin. An icon is fetched the first time a component asks for it
    /// and kept on disk after that, so a definition opened on a machine that has already seen the
    /// icon draws it without a request.
    ///
    /// The names are the ones listed on https://phosphoricons.com. A weight is a suffix, as in the
    /// catalogue: gear-six is the regular weight, gear-six-bold and gear-six-fill are variants.
    /// </summary>
    internal static class PhosphorIcons
    {
        /// <summary>Pinned, so the same name still resolves to the same drawing next year.</summary>
        const string Release = "2.1.1";

        static readonly string[] Weights = { "thin", "light", "bold", "fill", "duotone" };

        static readonly HttpClient Web = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        /// <summary>Name to SVG. An empty string marks a name the catalogue does not have.</summary>
        static readonly ConcurrentDictionary<string, string> Held =
            new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static readonly ConcurrentDictionary<string, byte> Asking =
            new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Raised on a background thread when a fetch lands, with the name that arrived.</summary>
        public static event Action<string> Arrived;

        public static string Folder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PillScript", "icons");

        /// <summary>
        /// The drawing for a name, or null while it is being fetched or if there is no such icon.
        /// A later call returns it, which is why callers redraw on Arrived.
        /// </summary>
        public static string Svg(string name)
        {
            var clean = Clean(name);
            if (clean == null) return null;

            if (Held.TryGetValue(clean, out var held)) return held.Length == 0 ? null : held;

            var file = Path.Combine(Folder, clean + ".svg");

            if (File.Exists(file))
            {
                try
                {
                    var text = File.ReadAllText(file);
                    Held[clean] = text;
                    return text;
                }
                catch (IOException)
                {
                    return null;
                }
            }

            Fetch(clean);
            return null;
        }

        /// <summary>A name is lowercase letters, digits and hyphens, and nothing else.</summary>
        public static string Clean(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            var trimmed = name.Trim().ToLowerInvariant();

            foreach (var letter in trimmed)
            {
                if (letter >= 'a' && letter <= 'z') continue;
                if (letter >= '0' && letter <= '9') continue;
                if (letter == '-') continue;

                return null;
            }

            return trimmed;
        }

        static void Fetch(string name)
        {
            if (!Asking.TryAdd(name, 0)) return;

            Task.Run(async () =>
            {
                try
                {
                    var answer = await Web.GetAsync(Address(name));

                    if (!answer.IsSuccessStatusCode)
                    {
                        // No such icon. Recorded, so a typo is not fetched again on every call.
                        Held[name] = string.Empty;
                        return;
                    }

                    var text = await answer.Content.ReadAsStringAsync();

                    Directory.CreateDirectory(Folder);
                    File.WriteAllText(Path.Combine(Folder, name + ".svg"), text);

                    Held[name] = text;
                    Arrived?.Invoke(name);
                }
                catch (Exception)
                {
                    // Offline, or the request failed. Not recorded, so the next call retries.
                }
                finally
                {
                    Asking.TryRemove(name, out _);
                }
            });
        }

        static string Address(string name)
        {
            var weight = "regular";

            foreach (var candidate in Weights)
            {
                if (!name.EndsWith("-" + candidate, StringComparison.Ordinal)) continue;

                weight = candidate;
                break;
            }

            return "https://unpkg.com/@phosphor-icons/core@" + Release
                   + "/assets/" + weight + "/" + name + ".svg";
        }
    }
}
