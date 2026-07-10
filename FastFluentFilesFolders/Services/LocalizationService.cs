using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.IO;

namespace FastFluentFilesFolders.Services
{
    public partial class LocalizationService : ObservableObject
    {
        private static readonly string StringsDir =
            Path.Combine(AppContext.BaseDirectory, "Strings");

        private Dictionary<string, string> _strings = new();

        [ObservableProperty]
        private string _currentLanguage = "zh-Hans";

        public LocalizationService()
        {
            LoadStrings();
        }

        public string GetString(string key)
        {
            return _strings.TryGetValue(key, out var value) ? value : key;
        }

        public void SetLanguage(string language)
        {
            if (string.IsNullOrEmpty(language) || CurrentLanguage == language)
                return;

            CurrentLanguage = language;
            LoadStrings();
        }

        private void LoadStrings()
        {
            var fileName = CurrentLanguage == "en" ? "en.json" : "zh-Hans.json";
            var filePath = Path.Combine(StringsDir, fileName);

            if (!File.Exists(filePath))
            {
                _strings = new Dictionary<string, string>();
                return;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                _strings = ParseSimpleJson(json);
            }
            catch
            {
                _strings = new Dictionary<string, string>();
            }
        }

        private static Dictionary<string, string> ParseSimpleJson(string json)
        {
            var dict = new Dictionary<string, string>();
            var span = json.AsSpan().Trim();
            if (span.Length < 2 || span[0] != '{' || span[^1] != '}')
                return dict;

            span = span.Slice(1, span.Length - 2).Trim();
            if (span.IsEmpty)
                return dict;

            var pos = 0;
            while (pos < span.Length)
            {
                pos = SkipWhitespace(span, pos);
                if (pos >= span.Length) break;

                var keyStart = span.Slice(pos).IndexOf('"');
                if (keyStart < 0) break;
                pos += keyStart + 1;

                var keyEnd = span.Slice(pos).IndexOf('"');
                if (keyEnd < 0) break;
                var key = span.Slice(pos, keyEnd).ToString();
                pos += keyEnd + 1;

                pos = SkipWhitespace(span, pos);
                if (pos >= span.Length || span[pos] != ':') break;
                pos = SkipWhitespace(span, pos + 1);

                if (pos >= span.Length || span[pos] != '"') break;
                pos++;

                var valueEnd = span.Slice(pos).IndexOf('"');
                if (valueEnd < 0) break;
                var value = span.Slice(pos, valueEnd).ToString();
                pos += valueEnd + 1;

                dict[key] = value;

                pos = SkipWhitespace(span, pos);
                if (pos < span.Length && span[pos] == ',') pos++;
            }

            return dict;
        }

        private static int SkipWhitespace(ReadOnlySpan<char> s, int start)
        {
            while (start < s.Length && char.IsWhiteSpace(s[start]))
                start++;
            return start;
        }
    }
}
