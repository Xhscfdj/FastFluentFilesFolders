using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FastFluentFilesFolders.ViewModels
{
    public static class ArchiveHelper
    {
        private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz",
            ".cab", ".iso", ".tgz", ".tar.gz", ".tar.bz2", ".tar.xz",
            ".lzh", ".arj", ".z", ".lz", ".lzma", ".lzo", ".sz", ".zst",
            ".mrpack"
        };

        public static bool IsArchiveExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension)) return false;
            return ArchiveExtensions.Contains(extension);
        }

        public static bool IsArchiveVirtualPath(string path, out string archiveFilePath, out string relativePath)
        {
            archiveFilePath = "";
            relativePath = "";

            if (string.IsNullOrEmpty(path)) return false;

            var normalized = path.Replace('/', '\\');
            var parts = normalized.Split('\\', StringSplitOptions.None);

            for (int i = 0; i < parts.Length; i++)
            {
                var ext = Path.GetExtension(parts[i]);
                if (IsArchiveExtension(ext))
                {
                    var candidate = string.Join("\\", parts.Take(i + 1));
                    if (File.Exists(candidate))
                    {
                        archiveFilePath = candidate;
                        relativePath = string.Join("\\", parts.Skip(i + 1).Where(p => !string.IsNullOrEmpty(p)));
                        return true;
                    }
                }
            }

            return false;
        }

        public static bool IsArchiveVirtualPath(string path)
        {
            return IsArchiveVirtualPath(path, out _, out _);
        }

        public static string GetArchiveRootVirtualPath(string archiveFilePath)
        {
            return archiveFilePath + "\\";
        }

        public static string GetParentOfArchiveRoot(string archiveFilePath)
        {
            return Path.GetDirectoryName(archiveFilePath) ?? Path.GetPathRoot(archiveFilePath) ?? "";
        }

        public static string GetParentInArchive(string archiveFilePath, string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                return GetParentOfArchiveRoot(archiveFilePath);

            var parts = relativePath.TrimEnd('\\').Split('\\');
            if (parts.Length <= 1)
                return GetArchiveRootVirtualPath(archiveFilePath);

            var parentRelative = string.Join("\\", parts.Take(parts.Length - 1));
            return GetArchiveRootVirtualPath(archiveFilePath) + parentRelative + "\\";
        }

        public static string CombineArchiveVirtualPath(string archiveFilePath, string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                return GetArchiveRootVirtualPath(archiveFilePath);

            return GetArchiveRootVirtualPath(archiveFilePath) + relativePath.TrimEnd('\\') + "\\";
        }
    }
}
