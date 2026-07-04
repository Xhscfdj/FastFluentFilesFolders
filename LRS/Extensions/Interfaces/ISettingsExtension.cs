using Microsoft.UI.Xaml;
using System.Collections.Generic;

namespace LRS.Extensions.Interfaces
{
    public interface ISettingsExtension : IExtension
    {
        IEnumerable<UIElement> CreateSettingsCards();
    }
}
