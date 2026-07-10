using Microsoft.UI.Xaml;
using System.Collections.Generic;

namespace FastFluentFilesFolders.Extensions.Interfaces
{
    public interface ISettingsExtension : IExtension
    {
        IEnumerable<UIElement> CreateSettingsCards();
    }
}
