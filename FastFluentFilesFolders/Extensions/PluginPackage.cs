using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;

namespace FastFluentFilesFolders.Extensions
{
    public class PluginPackage
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Version { get; set; } = "1.0.0";
        public string Author { get; set; } = "";
        public string Description { get; set; } = "";
        public string EntryAssembly { get; set; } = "";
        public string? MinAppVersion { get; set; }

        [JsonIgnore]
        public string InstallPath { get; set; } = "";

        [JsonIgnore]
        public bool IsExternal { get; set; } = true;

        [JsonIgnore]
        public Version ParsedVersion => System.Version.TryParse(Version, out var v) ? v : new Version(0, 0, 0);

        public static PluginPackage FromJson(string json)
        {
            return System.Text.Json.JsonSerializer.Deserialize<PluginPackage>(json)
                   ?? new PluginPackage();
        }

        public string ToJson()
        {
            return System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
    }

    public class InstalledPluginsManifest
    {
        public List<PluginPackage> Plugins { get; set; } = new();

        public static InstalledPluginsManifest LoadOrCreate(string directory)
        {
            var path = Path.Combine(directory, "plugins.json");
            if (File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    return System.Text.Json.JsonSerializer.Deserialize<InstalledPluginsManifest>(json)
                           ?? new InstalledPluginsManifest();
                }
                catch
                {
                    return new InstalledPluginsManifest();
                }
            }
            return new InstalledPluginsManifest();
        }

        public void Save(string directory)
        {
            var path = Path.Combine(directory, "plugins.json");
            var json = System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(path, json);
        }
    }
}
