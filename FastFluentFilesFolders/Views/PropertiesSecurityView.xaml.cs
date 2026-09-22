using FastFluentFilesFolders.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Security.Principal;
using System.Threading.Tasks;

namespace FastFluentFilesFolders.Views
{
	/// <summary>安全页：纯 XAML 绑定 ViewModel；仅警告确认与高级对话框在视图代码隐藏中。</summary>
	public sealed partial class PropertiesSecurityView : UserControl
	{
		public PropertiesSecurityViewModel Vm { get; }

		public PropertiesSecurityView(FileSystemNodeViewModel item)
		{
			InitializeComponent();
			Vm = new PropertiesSecurityViewModel(item);
			DataContext = Vm;
		}

		/// <summary>应用权限修改；修改系统资源前弹警告确认框。</summary>
		public async Task<bool> Apply()
		{
			if (Vm.NeedsWarningBeforeApply)
			{
				var confirm = await ConfirmPermissionChangeAsync();
				if (!confirm)
					return false;
			}

			var applied = await Task.Run(() => Vm.ApplyCore());
			if (!applied)
			{
				ShowError(Vm.LastError ?? App.ML.Get("CmdError"));
				return false;
			}

			Vm.ReloadState();
			return true;
		}

		private async Task<bool> ConfirmPermissionChangeAsync()
		{
			try
			{
				var dialog = new ContentDialog
				{
					Title = App.ML.Get("PropertiesSecurityConfirmTitle"),
					Content = new TextBlock
					{
						Text = App.ML.Get("PropertiesSecurityConfirmSystemWarning"),
						TextWrapping = TextWrapping.Wrap
					},
					PrimaryButtonText = App.ML.Get("PropertiesSecurityContinue"),
					CloseButtonText = App.ML.Get("CmdCancel"),
					DefaultButton = ContentDialogButton.Primary,
					XamlRoot = XamlRoot
				};
				return await dialog.ShowAsync() == ContentDialogResult.Primary;
			}
			catch
			{
				return false;
			}
		}

		private void OnAdvancedClick(object sender, RoutedEventArgs e) => ShowSecurityAdvancedDialog();

		private void ShowSecurityAdvancedDialog()
		{
			try
			{
				var lines = Vm.BuildAdvancedLines();
				var dialog = new ContentDialog
				{
					Title = App.ML.Get("PropertiesSecurityAdvancedTitle"),
					Content = new ScrollViewer
					{
						Content = new ItemsControl { ItemsSource = lines },
						MaxHeight = 420
					},
					CloseButtonText = App.ML.Get("CmdClose"),
					XamlRoot = XamlRoot
				};
				_ = dialog.ShowAsync();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[Properties] Security advanced dialog failed: {ex.Message}");
			}
		}

		/// <summary>内置“选择用户或组”弹窗，选择新的所有者（点击应用/确定后写入）。</summary>
		private async void OnChangeOwnerClick(object sender, RoutedEventArgs e)
		{
			try
			{
				var nameBox = new TextBox
				{
					Text = Vm.SuggestedOwnerValue,
					PlaceholderText = App.ML.Get("PropertiesSecurityOwnerPickerPlaceholder")
				};

				var error = new InfoBar
				{
					IsOpen = false,
					IsClosable = false,
					Severity = InfoBarSeverity.Error,
					Message = App.ML.Get("PropertiesSecurityOwnerInvalid")
				};

				var principalList = new ListView
				{
					ItemsSource = BuildCommonPrincipals(),
					SelectionMode = ListViewSelectionMode.Single,
					MaxHeight = 180
				};
				principalList.SelectionChanged += (_, _) =>
				{
					if (principalList.SelectedItem is string name)
					{
						nameBox.Text = name;
						error.IsOpen = false;
					}
				};

				var panel = new StackPanel { Spacing = 8, MinWidth = 360 };
				panel.Children.Add(new TextBlock
				{
					Text = App.ML.Get("PropertiesSecurityOwnerPickerPrompt"),
					TextWrapping = TextWrapping.Wrap
				});
				panel.Children.Add(nameBox);
				panel.Children.Add(new TextBlock
				{
					Text = App.ML.Get("PropertiesSecurityOwnerPickerBrowse"),
					FontSize = 12,
					Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray)
				});
				panel.Children.Add(principalList);
				panel.Children.Add(error);

				var dialog = new ContentDialog
				{
					Title = App.ML.Get("PropertiesSecurityOwnerPickerTitle"),
					Content = new ScrollViewer { Content = panel, MaxHeight = 460 },
					PrimaryButtonText = App.ML.Get("PropertiesSecurityOwnerPickerConfirm"),
					CloseButtonText = App.ML.Get("CmdCancel"),
					DefaultButton = ContentDialogButton.Primary,
					XamlRoot = XamlRoot
				};
				dialog.PrimaryButtonClick += (_, args) =>
				{
					if (!Vm.TrySetOwner(nameBox.Text))
					{
						args.Cancel = true;
						error.IsOpen = true;
					}
				};
				_ = await dialog.ShowAsync();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[Properties] Owner picker failed: {ex.Message}");
			}
		}

		private static List<string> BuildCommonPrincipals()
		{
			var result = new List<string>();
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			void TryAdd(SecurityIdentifier? sid)
			{
				if (sid == null) return;
				try
				{
					var name = sid.Translate(typeof(NTAccount)).Value;
					if (!string.IsNullOrEmpty(name) && seen.Add(name))
						result.Add(name);
				}
				catch { }
			}

			TryAdd(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
			TryAdd(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null));
			TryAdd(new SecurityIdentifier(WellKnownSidType.WorldSid, null));
			TryAdd(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
			TryAdd(new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null));
			TryAdd(new SecurityIdentifier(WellKnownSidType.NetworkServiceSid, null));
			// TrustedInstaller
			try { TryAdd(new SecurityIdentifier("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464")); } catch { }
			try { TryAdd(WindowsIdentity.GetCurrent().User); } catch { }

			return result;
		}

		private void ShowError(string message)
		{
			try
			{
				var dialog = new ContentDialog
				{
					Title = App.ML.Get("CmdError"),
					Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
					CloseButtonText = App.ML.Get("CmdOk"),
					XamlRoot = XamlRoot
				};
				_ = dialog.ShowAsync();
			}
			catch { }
		}
	}
}
