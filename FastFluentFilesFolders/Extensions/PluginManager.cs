using FastFluentFilesFolders.Extensions.Interfaces;
using FastFluentFilesFolders.Services;
using FastFluentFilesFolders.ViewModels;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace FastFluentFilesFolders.Extensions
{
    public class ExternalPluginEntry
    {
        public PluginPackage Package { get; set; } = null!;
        public IExtension? Extension { get; set; }
        public ExternalPluginLoadContext? LoadContext { get; set; }
    }

    public class PluginManager
    {
        private readonly List<IExtension> _extensions = new();
        private readonly List<ExternalPluginEntry> _externalEntries = new();
        private readonly IServiceProvider _services;
        private DispatcherQueue? _dispatcherQueue;
        private bool _isLoaded;

        public event Action? ExtensionsChanged;

        public PluginManager(IServiceProvider services)
        {
            _services = services;
        }

        public void SetDispatcherQueue(DispatcherQueue dispatcherQueue)
        {
            _dispatcherQueue = dispatcherQueue;
        }

        public IReadOnlyList<IExtension> Extensions => _extensions.AsReadOnly();
        public IReadOnlyList<ExternalPluginEntry> ExternalPluginEntries => _externalEntries.AsReadOnly();

        public IEnumerable<T> GetExtensionsOfType<T>() where T : IExtension
            => _extensions.OfType<T>();

        public async Task LoadAllAsync()
        {
            if (_isLoaded) return;

            var allExtensions = _services.GetServices<IExtension>();
            var configs = _services.GetRequiredService<Configs>();
            var loc = _services.GetRequiredService<LocalizationService>();

            var context = new ExtensionContext(_services, _dispatcherQueue, configs, loc);

            foreach (var ext in allExtensions)
            {
                try
                {
                    await ext.InitializeAsync(context);
                    _extensions.Add(ext);
                    Debug.WriteLine($"[PluginManager] Loaded built-in: {ext.Id} v{ext.Version}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PluginManager] Failed to load {ext.Id}: {ex.Message}");
                }
            }

            _isLoaded = true;
            ExtensionsChanged?.Invoke();

            await LoadExternalPluginsAsync();
        }

        public async Task LoadExternalPluginsAsync()
        {
            var manifest = PluginImporter.LoadManifest();
            var configs = _services.GetRequiredService<Configs>();
            var loc = _services.GetRequiredService<LocalizationService>();
            var context = new ExtensionContext(_services, _dispatcherQueue, configs, loc);

            foreach (var package in manifest.Plugins)
            {
                if (_externalEntries.Any(e => e.Package.Id == package.Id))
                    continue;

                var installDir = Path.Combine(PluginImporter.PluginsDirectory, package.Id);
                if (!Directory.Exists(installDir))
                    continue;

                var ext = await ExternalPluginLoader.LoadPluginAsync(installDir, package);
                if (ext == null) continue;

                try
                {
                    await ext.InitializeAsync(context);
                    _externalEntries.Add(new ExternalPluginEntry
                    {
                        Package = package,
                        Extension = ext
                    });
                    _extensions.Add(ext);
                    Debug.WriteLine($"[PluginManager] Loaded external: {ext.Id} v{ext.Version}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PluginManager] Failed to init external plugin {package.Id}: {ex.Message}");
                }
            }

            ExtensionsChanged?.Invoke();
        }

        public async Task<(bool Success, string Message, PluginPackage? Package)> ImportAndLoadPluginAsync(string zipPath)
        {
            var (success, message, package) = await PluginImporter.ImportFromZipAsync(zipPath);
            if (!success || package == null)
                return (false, message, null);

            var configs = _services.GetRequiredService<Configs>();
            var loc = _services.GetRequiredService<LocalizationService>();
            var context = new ExtensionContext(_services, _dispatcherQueue, configs, loc);

            var ext = await ExternalPluginLoader.LoadPluginAsync(package.InstallPath, package);
            if (ext == null)
            {
                await PluginImporter.UninstallPluginAsync(package.Id);
                return (false, "PluginImport.LoadFailed", null);
            }

            try
            {
                await ext.InitializeAsync(context);
                _externalEntries.Add(new ExternalPluginEntry
                {
                    Package = package,
                    Extension = ext
                });
                _extensions.Add(ext);
                Debug.WriteLine($"[PluginManager] Imported & loaded: {ext.Id} v{ext.Version}");
                ExtensionsChanged?.Invoke();
                return (true, "PluginImport.Success", package);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PluginManager] Plugin init failed: {ex.Message}");
                return (false, "PluginImport.LoadFailed", null);
            }
        }

        public async Task<(bool Success, string Message)> UninstallPluginAsync(string pluginId)
        {
            var entry = _externalEntries.FirstOrDefault(e => e.Package.Id == pluginId);
            if (entry != null && entry.Extension != null)
            {
                try
                {
                    await entry.Extension.ShutdownAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PluginManager] Error shutting down {pluginId}: {ex.Message}");
                }
                _extensions.Remove(entry.Extension);
                _externalEntries.Remove(entry);
            }

            var result = await PluginImporter.UninstallPluginAsync(pluginId);
            ExtensionsChanged?.Invoke();
            return result;
        }

        public async Task ShutdownAsync()
        {
            foreach (var ext in _extensions)
            {
                try
                {
                    await ext.ShutdownAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PluginManager] Failed to shutdown {ext.Id}: {ex.Message}");
                }
            }
            _extensions.Clear();
            _externalEntries.Clear();
            _isLoaded = false;
        }

        public IEnumerable<IContextMenuExtension> GetContextMenuPlugins()
            => _extensions.OfType<IContextMenuExtension>();

        public IEnumerable<IToolbarExtension> GetToolbarPlugins()
            => _extensions.OfType<IToolbarExtension>();

        public IEnumerable<ISettingsExtension> GetSettingsPlugins()
            => _extensions.OfType<ISettingsExtension>();

        public IEnumerable<IFileOperationHook> GetOperationHooks()
            => _extensions.OfType<IFileOperationHook>();

        public IEnumerable<ICustomThemeExtension> GetThemePlugins()
            => _extensions.OfType<ICustomThemeExtension>();

        public IEnumerable<IArchiveBrowser> GetArchiveBrowsers()
            => _extensions.OfType<IArchiveBrowser>();

        public async Task<bool> RunBeforeOperationHooks(FileOperationContext context)
        {
            foreach (var hook in GetOperationHooks())
            {
                try
                {
                    var result = await hook.BeforeExecute(context);
                    if (!result || context.IsCancelled)
                        return false;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PluginManager] Hook {hook.Id} BeforeExecute error: {ex.Message}");
                }
            }
            return true;
        }

        public async Task RunAfterOperationHooks(FileOperationContext context, bool success)
        {
            foreach (var hook in GetOperationHooks())
            {
                try
                {
                    await hook.AfterExecute(context, success);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PluginManager] Hook {hook.Id} AfterExecute error: {ex.Message}");
                }
            }
        }
    }
}
