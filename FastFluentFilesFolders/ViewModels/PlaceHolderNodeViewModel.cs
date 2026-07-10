

namespace FastFluentFilesFolders.ViewModels
{
	public class PlaceholderNodeViewModel : FileSystemNodeViewModel
	{
		public PlaceholderNodeViewModel() : base("C:\\", false, true, null, null, true)
		{
			IsPlaceholder = true;
			Name = "Loading...";
		}
	}
}