using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace LRS.Models
{
	public class BreadcrumbSegment
	{
		public string DisplayName { get; set; }
		public string FullPath { get; set; }
		public bool IsLast { get; set; }
		public ICommand NavigateCommand { get; set; }        // 导航到当前路径
		public ICommand NavigateSubCommand { get; set; }    // 导航到子文件夹
		public ObservableCollection<string> SubFolders { get; set; } = new();
	}
}
