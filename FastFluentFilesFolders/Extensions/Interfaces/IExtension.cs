using System;
using System.Threading.Tasks;

namespace FastFluentFilesFolders.Extensions.Interfaces
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
