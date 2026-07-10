using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FastFluentFilesFolders.Extensions.Interfaces
{
    public readonly struct ArchiveEntry
    {
        public string Name { get; init; }
        public string RelativePath { get; init; }
        public bool IsDirectory { get; init; }
        public long Size { get; init; }
        public DateTime LastModified { get; init; }
    }

    public interface IArchiveBrowser : IExtension
    {
        bool CanBrowse(string filePath);
        Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(string archivePath, string relativePath);
        Task<string?> ExtractEntryToTempAsync(string archivePath, string relativePath);
    }
}
