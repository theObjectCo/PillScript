using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace PillScript.Scripting
{
    internal sealed class PackageHit
    {
        public string Id;
        public string Version;
        public string Description;
        public long Downloads;
    }

    /// <summary>
    /// Looks packages up on nuget.org, so a package can be found by name. None of this is needed
    /// to build; it only fills in the references window.
    /// </summary>
    internal static class NuGetCatalogue
    {
        // Every await here drops the caller's context deliberately. Nothing in this class touches
        // the UI, and a continuation that required the UI thread would deadlock a caller waiting on
        // the task from that thread.

        const string Search = "https://azuresearch-usnc.nuget.org/query";
        const string Flat = "https://api.nuget.org/v3-flatcontainer";

        static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };

        /// <summary>Packages matching a search term, most used first, as nuget.org orders them.</summary>
        public static async Task<List<PackageHit>> SearchAsync(string term)
        {
            var hits = new List<PackageHit>();
            if (string.IsNullOrWhiteSpace(term)) return hits;

            var url = Search + "?q=" + Uri.EscapeDataString(term.Trim()) + "&take=20&prerelease=false";
            var json = await Get(url).ConfigureAwait(false);

            if (json == null) return hits;

            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("data", out var data)) return hits;

            foreach (var entry in data.EnumerateArray())
            {
                hits.Add(new PackageHit
                {
                    Id = Text(entry, "id"),
                    Version = Text(entry, "version"),
                    Description = Text(entry, "description"),
                    Downloads = entry.TryGetProperty("totalDownloads", out var total)
                                && total.TryGetInt64(out var count)
                        ? count
                        : 0
                });
            }

            return hits;
        }

        /// <summary>Every published version of one package, newest first.</summary>
        public static async Task<List<string>> VersionsAsync(string id)
        {
            var versions = new List<string>();
            if (string.IsNullOrWhiteSpace(id)) return versions;

            var json = await Get(Flat + "/" + Uri.EscapeDataString(id.Trim().ToLowerInvariant())
                + "/index.json").ConfigureAwait(false);
            if (json == null) return versions;

            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("versions", out var list)) return versions;

            versions.AddRange(list.EnumerateArray().Select(v => v.GetString()).Where(v => v != null));
            versions.Reverse();

            return versions;
        }

        static async Task<string> Get(string url)
        {
            try
            {
                using var response = await Client.GetAsync(url).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Offline, or nuget.org returned an error. The window reports it and carries on.
                return null;
            }
        }

        static string Text(JsonElement element, string name)
            => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : string.Empty;
    }
}
