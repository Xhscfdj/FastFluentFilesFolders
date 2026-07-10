using FastFluentFilesFolders.Extensions.Interfaces;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;

namespace FastFluentFilesFolders.Extensions
{
    public class ExternalPluginLoadContext : AssemblyLoadContext
    {
        private readonly string _pluginDir;

        public ExternalPluginLoadContext(string pluginDir) : base(isCollectible: true)
        {
            _pluginDir = pluginDir;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var dllPath = Path.Combine(_pluginDir, $"{assemblyName.Name}.dll");
            if (File.Exists(dllPath))
            {
                return LoadFromAssemblyPath(dllPath);
            }
            return null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var dllPath = Path.Combine(_pluginDir, unmanagedDllName);
            if (File.Exists(dllPath))
            {
                return LoadUnmanagedDllFromPath(dllPath);
            }
            return IntPtr.Zero;
        }
    }

    public class ExternalPluginLoader
    {
        public static async Task<IExtension?> LoadPluginAsync(string pluginDir, PluginPackage package)
        {
            return await Task.Run(() => LoadPlugin(pluginDir, package));
        }

        private static IExtension? LoadPlugin(string pluginDir, PluginPackage package)
        {
            try
            {
                var dllPath = Path.Combine(pluginDir, package.EntryAssembly);
                if (!File.Exists(dllPath))
                {
                    Debug.WriteLine($"[ExternalPluginLoader] DLL not found: {dllPath}");
                    return null;
                }

                var loadContext = new ExternalPluginLoadContext(pluginDir);
                var assembly = loadContext.LoadFromAssemblyPath(dllPath);

                var extensionTypes = assembly.GetTypes()
                    .Where(t => typeof(IExtension).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
                    .ToList();

                if (extensionTypes.Count == 0)
                {
                    Debug.WriteLine($"[ExternalPluginLoader] No IExtension implementation found in {package.Id}");
                    return null;
                }

                var extType = extensionTypes[0];
                var ext = (IExtension?)Activator.CreateInstance(extType);
                if (ext == null)
                {
                    Debug.WriteLine($"[ExternalPluginLoader] Failed to create instance of {extType.FullName}");
                    return null;
                }

                Debug.WriteLine($"[ExternalPluginLoader] Loaded external plugin: {package.Id} from {dllPath}");
                return ext;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ExternalPluginLoader] Failed to load {package.Id}: {ex.Message}");
                return null;
            }
        }
    }
}
