using System.Threading.Tasks;

namespace FastFluentFilesFolders.Extensions.Interfaces
{
    public enum FileOperationType
    {
        Copy,
        Move,
        Delete,
        Rename,
        Create
    }

    public class FileOperationContext
    {
        public FileOperationType OperationType { get; set; }
        public string SourcePath { get; set; } = "";
        public string? DestinationPath { get; set; }
        public bool IsDirectory { get; set; }
        public bool IsCancelled { get; set; }
    }

    public interface IFileOperationHook : IExtension
    {
        Task<bool> BeforeExecute(FileOperationContext context);
        Task AfterExecute(FileOperationContext context, bool success);
    }
}
