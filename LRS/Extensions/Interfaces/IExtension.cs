using System;
using System.Threading.Tasks;

namespace LRS.Extensions.Interfaces
{
    public interface IExtension
    {
        string Id { get; }
        string DisplayName { get; }
        Version Version { get; }

        Task InitializeAsync(ExtensionContext context);
        Task ShutdownAsync();
    }
}
