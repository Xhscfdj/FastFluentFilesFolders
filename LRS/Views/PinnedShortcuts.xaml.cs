using LRS.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LRS.Views
{
	public sealed partial class PinnedShortcuts : Page
	{
		private MainWindowViewModel VM => App.SharedViewModel;

		public PinnedShortcuts()
		{
			InitializeComponent();
			DataContext = App.SharedViewModel;
		}

		private void ListView_ItemClick(object sender, ItemClickEventArgs e)
		{
			if (e.ClickedItem is FileSystemNodeViewModel node && node.IsDirectory)
			{
				VM.SelectedFolder = node;
			}
		}
	}
}
