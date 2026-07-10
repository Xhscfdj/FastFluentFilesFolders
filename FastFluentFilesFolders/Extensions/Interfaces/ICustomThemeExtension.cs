using Microsoft.UI.Xaml;

namespace FastFluentFilesFolders.Extensions.Interfaces
{
    public interface ICustomThemeExtension : IExtension
    {
        ResourceDictionary? GetThemeResources();
        string ThemeName { get; }
    }
}
