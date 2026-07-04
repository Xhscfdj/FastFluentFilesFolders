using LRS.ViewModels;
using System.Collections.Generic;

namespace LRS.Extensions.Interfaces
{
    public enum ContextMenuLocation
    {
        FileItem,
        FolderItem,
        Background,
        MultipleSelection
    }

    public class ExtensionMenuItem
    {
        public string Header { get; set; } = "";
        public string? IconGlyph { get; set; }
        public string? ThemedIconKey { get; set; }
        public System.Windows.Input.ICommand? Command { get; set; }
        public object? CommandParameter { get; set; }
        public bool IsSeparator { get; set; }
        public List<ExtensionMenuItem>? SubItems { get; set; }
    }

    public interface IContextMenuExtension : IExtension
    {
        IEnumerable<ExtensionMenuItem> GetMenuItems(
            FileSystemNodeViewModel? targetNode,
            ContextMenuLocation location);
    }
}
