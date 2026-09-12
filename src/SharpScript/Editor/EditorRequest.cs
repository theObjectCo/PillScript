using System.Linq;
using System.Text.Json;

namespace SharpScript.Editor
{
    /// <summary>
    /// One message from the page. Commands carry a name or some text; language queries carry a
    /// caret and an id the answer is sent back under. Every field is optional: the page sends
    /// only what the message it is making needs.
    /// </summary>
    internal sealed class EditorRequest
    {
        public string Type;
        public string Id;
        public string Name;
        public string Content;
        public string From;
        public string To;
        public string File;
        public string Text;
        public string Trigger;
        public string Action;
        public bool Enabled;
        public int Offset;
        public int Index;
        public int Cols;
        public int Rows;
        public int[] Lines = new int[0];

        public static EditorRequest Parse(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("type", out var type)) return null;

            return new EditorRequest
            {
                Type = type.GetString(),
                Id = Text_(root, "id"),
                Name = Text_(root, "name"),
                Content = Text_(root, "content"),
                From = Text_(root, "from"),
                To = Text_(root, "to"),
                File = Text_(root, "file"),
                Text = Text_(root, "text"),
                Trigger = Text_(root, "trigger"),
                Action = Text_(root, "action"),
                Enabled = Flag(root, "enabled"),
                Offset = Number(root, "offset"),
                Index = Number(root, "index"),
                Cols = Number(root, "cols"),
                Rows = Number(root, "rows"),
                Lines = Numbers(root, "lines")
            };
        }

        static string Text_(JsonElement root, string name)
            => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : string.Empty;

        static bool Flag(JsonElement root, string name)
            => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

        static int[] Numbers(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
                return new int[0];

            return value.EnumerateArray()
                .Where(item => item.TryGetInt32(out _))
                .Select(item => item.GetInt32())
                .ToArray();
        }

        static int Number(JsonElement root, string name)
            => root.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
                ? number
                : 0;
    }
}
