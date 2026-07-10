using System.Collections.Generic;
using System.Windows.Input;

namespace FastFluentFilesFolders.Extensions.Interfaces
{
    public class ToolbarItem
    {
        public string Header { get; set; } = "";
        public string? IconGlyph { get; set; }
        public ICommand? Command { get; set; }
        public object? CommandParameter { get; set; }
        public string? ToolTip { get; set; }
    }

    public interface IToolbarExtension : IExtension
    {
        IEnumerable<ToolbarItem> GetToolbarItems();
    }
}
