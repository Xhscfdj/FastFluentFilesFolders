using Microsoft.UI.Xaml;

namespace LRS.Extensions.Interfaces
{
    public interface ICustomThemeExtension : IExtension
    {
        ResourceDictionary? GetThemeResources();
        string ThemeName { get; }
    }
}
