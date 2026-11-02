

namespace FastFluentFilesFolders.ViewModels
{
	public partial class PlaceholderNodeViewModel : FileSystemNodeViewModel
	{
		public PlaceholderNodeViewModel() : base("C:\\", false, true, null, null, true)
		{
			IsPlaceholder = true;
			Name = App.ML?.SearchLoading ?? "Loading...";
		}
	}
}