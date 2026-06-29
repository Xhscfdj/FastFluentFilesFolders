using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LRS.Services
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
                using var doc = JsonDocument.Parse(json);
                var dict = new Dictionary<string, string>();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    dict[prop.Name] = prop.Value.GetString() ?? "";
                }
                _strings = dict;
            }
            catch
            {
                _strings = new Dictionary<string, string>();
            }
        }
    }
}
