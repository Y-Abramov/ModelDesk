using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ModelDesk
{
    internal class ModelDeskSettings
    {
        public List<string>              OpenIds      { get; } = new List<string>();
        public List<string>              Groups       { get; } = new List<string>();
        public Dictionary<string,string> ModelGroups  { get; } = new Dictionary<string,string>(StringComparer.Ordinal);
        public Dictionary<string,string> ModelStatuses{ get; } = new Dictionary<string,string>(StringComparer.Ordinal);
        public Dictionary<string,string> ModelNotes   { get; } = new Dictionary<string,string>(StringComparer.Ordinal);

        // ── Load ──────────────────────────────────────────────────────────────

        public static ModelDeskSettings Load(string path)
        {
            var s = new ModelDeskSettings();
            if (!File.Exists(path)) return s;
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8).Trim();
                if (json.StartsWith("["))
                {
                    // Legacy format: bare array of open IDs
                    s.OpenIds.AddRange(ParseArray(json));
                    return s;
                }
                s.OpenIds.AddRange(ParseArray(Section(json, "open")));
                s.Groups.AddRange(ParseArray(Section(json, "groups")));
                foreach (var kv in ParseDict(Section(json, "modelGroups")))
                    s.ModelGroups[kv.Key] = kv.Value;
                foreach (var kv in ParseDict(Section(json, "modelStatuses")))
                    s.ModelStatuses[kv.Key] = kv.Value;
                foreach (var kv in ParseDict(Section(json, "modelNotes")))
                    s.ModelNotes[kv.Key] = kv.Value;
            }
            catch { }
            return s;
        }

        // ── Save ──────────────────────────────────────────────────────────────

        public void Save(string path)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"open\": "         + JArr(OpenIds) + ",");
                sb.AppendLine("  \"groups\": "       + JArr(Groups)  + ",");
                sb.AppendLine("  \"modelGroups\": "  + JObj(ModelGroups) + ",");
                sb.AppendLine("  \"modelStatuses\": " + JObj(ModelStatuses) + ",");
                sb.Append    ("  \"modelNotes\": "    + JObj(ModelNotes));
                sb.AppendLine();
                sb.Append("}");
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string E(string s) =>
            (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static string JArr(IEnumerable<string> items)
        {
            var parts = new List<string>();
            foreach (var s in items) parts.Add("\"" + E(s) + "\"");
            return "[" + string.Join(", ", parts) + "]";
        }

        private static string JObj(Dictionary<string,string> dict)
        {
            if (dict.Count == 0) return "{}";
            var parts = new List<string>();
            foreach (var kv in dict)
                parts.Add("    \"" + E(kv.Key) + "\": \"" + E(kv.Value) + "\"");
            return "{\n" + string.Join(",\n", parts) + "\n  }";
        }

        private static List<string> ParseArray(string json)
        {
            var r = new List<string>();
            if (string.IsNullOrWhiteSpace(json)) return r;
            foreach (Match m in Regex.Matches(json, @"""((?:[^""\\]|\\.)*)"""))
                r.Add(Unescape(m.Groups[1].Value));
            return r;
        }

        private static Dictionary<string,string> ParseDict(string json)
        {
            var r = new Dictionary<string,string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(json)) return r;
            foreach (Match m in Regex.Matches(json, @"""((?:[^""\\]|\\.)*)""\s*:\s*""((?:[^""\\]|\\.)*)"""))
                r[Unescape(m.Groups[1].Value)] = Unescape(m.Groups[2].Value);
            return r;
        }

        private static string Unescape(string s) =>
            s.Replace("\\\\", "\\").Replace("\\\"", "\"");

        private static string Section(string json, string key)
        {
            var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*");
            if (!m.Success) return "";
            int start = m.Index + m.Length;
            if (start >= json.Length) return "";
            char open  = json[start];
            char close = open == '[' ? ']' : open == '{' ? '}' : '\0';
            if (close == '\0') return "";
            int depth = 0;
            for (int i = start; i < json.Length; i++)
            {
                if (json[i] == open)  depth++;
                else if (json[i] == close) { if (--depth == 0) return json.Substring(start, i - start + 1); }
            }
            return "";
        }
    }
}
