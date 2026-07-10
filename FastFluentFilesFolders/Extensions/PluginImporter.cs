using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace FastFluentFilesFolders.Extensions
{
    public class PluginImporter
    {
        private static readonly string PluginsRootDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FastFluentFilesFolders", "Plugins");

        public static string PluginsDirectory
        {
            get
            {
                Directory.CreateDirectory(PluginsRootDir);
                return PluginsRootDir;
            }
        }

        public static InstalledPluginsManifest LoadManifest()
        {
            return InstalledPluginsManifest.LoadOrCreate(PluginsDirectory);
        }

        public static async Task<(bool Success, string Message, PluginPackage? Package)>
            ImportFromZipAsync(string zipPath)
        {
            return await Task.Run(() => ImportFromZip(zipPath));
        }

        private static (bool Success, string Message, PluginPackage? Package) ImportFromZip(string zipPath)
        {
            try
            {
                if (!File.Exists(zipPath))
                    return (false, "PluginImport.FileNotFound", null);

                using var archive = ZipFile.OpenRead(zipPath);

                var manifestEntry = archive.GetEntry("plugin.json");
                if (manifestEntry == null)
                    return (false, "PluginImport.NoManifest", null);

                PluginPackage package;
                using (var stream = manifestEntry.Open())
                using (var reader = new StreamReader(stream))
                {
                    var json = reader.ReadToEnd();
                    package = PluginPackage.FromJson(json);
                }

                if (string.IsNullOrWhiteSpace(package.Id))
                    return (false, "PluginImport.InvalidManifest", null);

                if (string.IsNullOrWhiteSpace(package.EntryAssembly))
                    return (false, "PluginImport.NoEntryAssembly", null);

                var dllEntry = archive.GetEntry(package.EntryAssembly);
                if (dllEntry == null)
                    return (false, "PluginImport.EntryNotFound", null);

                var installDir = Path.Combine(PluginsDirectory, package.Id);

                if (Directory.Exists(installDir))
                {
                    try { Directory.Delete(installDir, true); }
                    catch { return (false, "PluginImport.DirInUse", null); }
                }

                Directory.CreateDirectory(installDir);

                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;

                    var destPath = Path.Combine(installDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                    var destDir = Path.GetDirectoryName(destPath);
                    if (destDir != null) Directory.CreateDirectory(destDir);

                    entry.ExtractToFile(destPath, overwrite: true);
                }

                package.InstallPath = installDir;
                package.IsExternal = true;

                var manifest = LoadManifest();
                var existingIndex = manifest.Plugins.FindIndex(p => p.Id == package.Id);
                if (existingIndex >= 0)
                    manifest.Plugins[existingIndex] = package;
                else
                    manifest.Plugins.Add(package);
                manifest.Save(PluginsDirectory);

                Debug.WriteLine($"[PluginImporter] Imported: {package.Id} v{package.Version} to {installDir}");
                return (true, "PluginImport.Success", package);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PluginImporter] Import failed: {ex.Message}");
                return (false, "PluginImport.UnknownError", null);
            }
        }

        public static async Task<(bool Success, string Message)> UninstallPluginAsync(string pluginId)
        {
            return await Task.Run(() => UninstallPlugin(pluginId));
        }

        private static (bool Success, string Message) UninstallPlugin(string pluginId)
        {
            try
            {
                var installDir = Path.Combine(PluginsDirectory, pluginId);
                if (Directory.Exists(installDir))
                {
                    Directory.Delete(installDir, true);
                }

                var manifest = LoadManifest();
                manifest.Plugins.RemoveAll(p => p.Id == pluginId);
                manifest.Save(PluginsDirectory);

                Debug.WriteLine($"[PluginImporter] Uninstalled: {pluginId}");
                return (true, "PluginImport.Uninstalled");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PluginImporter] Uninstall failed: {ex.Message}");
                return (false, "PluginImport.UninstallFailed");
            }
        }

        public static PluginPackage? GetInstalledPackage(string pluginId)
        {
            var manifest = LoadManifest();
            return manifest.Plugins.FirstOrDefault(p => p.Id == pluginId);
        }

        public static bool IsPluginInstalled(string pluginId)
        {
            var installDir = Path.Combine(PluginsDirectory, pluginId);
            return Directory.Exists(installDir) &&
                   File.Exists(Path.Combine(installDir, "plugin.json"));
        }
    }
}
