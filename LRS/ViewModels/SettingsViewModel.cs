using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static LRS.UserControls.TreeDataGrid;

namespace LRS.ViewModels
{
	public class SettingsViewModel
	{
		public static List<KeyValuePair<SortMode, string>> OrderModePairs { get; } =
		new Dictionary<SortMode, string>
		{
			{ SortMode.NameAsc, "名称升序" },
			{ SortMode.NameDesc, "名称降序" },
			{ SortMode.SizeDesc, "大小降序" },
			{ SortMode.SizeAsc, "大小升序" },
			{ SortMode.ModifiedDesc, "修改时间降序" },
			{ SortMode.ModifiedAsc, "修改时间升序" },
			{ SortMode.CreatedDesc, "创建时间降序" },
			{ SortMode.CreatedAsc, "创建时间升序" },
		}.ToList();
		public enum SortModes
		{
			NameAsc,
			NameDesc,
			ModifiedAsc,
			ModifiedDesc,
			CreatedAsc,
			CreatedDesc,
			SizeAsc,
			SizeDesc,
		}
		public SettingsViewModel()
		{

		}
		public void UpdataDefaultOrderModeSettings(string orderMode)
		{
			
		}
	}
}
