using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FastFluentFilesFolders.ViewModels
{
	/// <summary>以前的版本页 ViewModel：查询并展示卷影副本。</summary>
	public partial class PropertiesPreviousVersionsViewModel : ObservableObject
	{
		private readonly FileSystemNodeViewModel _item;
		private bool _querying;

		public ObservableCollection<string> Versions { get; } = new();

		public string NoteText => App.ML.Get("PropertiesPreviousVersionsNote");
		public string LoadingText => App.ML.Get("PropertiesPreviousVersionsLoading");
		public string OpenLabel => App.ML.Get("PropertiesPreviousOpen");
		public string RestoreLabel => App.ML.Get("PropertiesPreviousRestore");

		[ObservableProperty] public partial bool IsLoading { get; set; }
		[ObservableProperty] public partial bool HasVersions { get; set; }
		[ObservableProperty] public partial bool HasEmptyMessage { get; set; }
		[ObservableProperty] public partial string EmptyMessage { get; set; } = string.Empty;

		public PropertiesPreviousVersionsViewModel(FileSystemNodeViewModel item)
		{
			_item = item;
		}

		public async Task LoadAsync()
		{
			if (_querying) return;
			_querying = true;
			IsLoading = true;
			HasEmptyMessage = false;
			try
			{
				var lines = await Task.Run(() => QueryPreviousVersions(_item.FullPath));
				var has = lines.Any(l => l.StartsWith("Shadow Copy Volume:", StringComparison.OrdinalIgnoreCase));
				Versions.Clear();
				if (has)
					foreach (var line in lines) Versions.Add(line);

				HasVersions = has;
				if (!has)
				{
					EmptyMessage = App.ML.Get("PropertiesPreviousVersionsNone");
					HasEmptyMessage = true;
				}
			}
			catch (Exception ex)
			{
				HasVersions = false;
				EmptyMessage = $"{App.ML.Get("PropertiesPreviousVersionsError")} ({ex.Message})";
				HasEmptyMessage = true;
			}
			finally
			{
				IsLoading = false;
				_querying = false;
			}
		}

		private static List<string> QueryPreviousVersions(string path)
		{
			var lines = new List<string>();
			try
			{
				var root = Path.GetPathRoot(path);
				if (string.IsNullOrWhiteSpace(root))
					return new List<string> { App.ML.Get("PropertiesPreviousVersionsNone") };

				var psi = new ProcessStartInfo("vssadmin", $"list shadows /for={root}")
				{
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					UseShellExecute = false,
					CreateNoWindow = true
				};
				using var process = Process.Start(psi);
				if (process == null)
					return new List<string> { App.ML.Get("PropertiesPreviousVersionsNone") };

				var output = process.StandardOutput.ReadToEnd();
				process.WaitForExit();

				foreach (var rawLine in output.Split('\n'))
				{
					var line = rawLine.Trim();
					if (line.Length == 0) continue;
					if (line.StartsWith("Shadow Copy ID:", StringComparison.OrdinalIgnoreCase) ||
					    line.StartsWith("Original Volume:", StringComparison.OrdinalIgnoreCase) ||
					    line.StartsWith("Shadow Copy Volume:", StringComparison.OrdinalIgnoreCase) ||
					    line.StartsWith("Originating Machine:", StringComparison.OrdinalIgnoreCase) ||
					    line.StartsWith("Service Machine:", StringComparison.OrdinalIgnoreCase) ||
					    line.StartsWith("Provider:", StringComparison.OrdinalIgnoreCase) ||
					    line.StartsWith("Type:", StringComparison.OrdinalIgnoreCase) ||
					    line.StartsWith("Attributes:", StringComparison.OrdinalIgnoreCase) ||
					    line.StartsWith("Creation Time:", StringComparison.OrdinalIgnoreCase))
					{
						lines.Add(line);
					}
				}

				if (lines.Count == 0)
					lines.Add(App.ML.Get("PropertiesPreviousVersionsNone"));
			}
			catch
			{
				lines.Add(App.ML.Get("PropertiesPreviousVersionsError"));
			}
			return lines;
		}
	}
}
