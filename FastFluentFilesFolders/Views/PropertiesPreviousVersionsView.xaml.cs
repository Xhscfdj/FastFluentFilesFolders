using FastFluentFilesFolders.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FastFluentFilesFolders.Views
{
	/// <summary>以前的版本页：纯 XAML 绑定 ViewModel；查询逻辑在 VM 中。</summary>
	public sealed partial class PropertiesPreviousVersionsView : UserControl
	{
		public PropertiesPreviousVersionsViewModel Vm { get; }

		public PropertiesPreviousVersionsView(FileSystemNodeViewModel item)
		{
			InitializeComponent();
			Vm = new PropertiesPreviousVersionsViewModel(item);
			DataContext = Vm;
			_ = Vm.LoadAsync();
		}

		/// <summary>再次进入该页时重新查询。</summary>
		public void Refresh() => _ = Vm.LoadAsync();
	}
}
