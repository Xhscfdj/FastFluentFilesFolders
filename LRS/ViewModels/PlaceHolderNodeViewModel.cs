

namespace LRS.ViewModels
{
	public class PlaceholderNodeViewModel : FileSystemNodeViewModel
	{
		public PlaceholderNodeViewModel() : base("C:\\", false, true, null, null)
		{
			IsPlaceholder = true;
			Name = "Loading...";
		}
	}
}