using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Windows.ApplicationModel;

namespace FastFluentFilesFolders.ViewModels
{
	/// <summary>兼容性页 ViewModel（仅 .exe）。</summary>
	public partial class PropertiesCompatibilityViewModel : ObservableObject
	{
		private const string CompatLayersKeyPath = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
		// reg.exe 需要以 HKCU\ 开头的完整根路径
		private const string RegLayersKey = @"HKCU\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
		private static readonly string RegExePath =
			System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "reg.exe");
		/// <summary>调试日志：同时输出到调试器与 %LOCALAPPDATA%\FastFluentFilesFolders\compat.log。</summary>
		private static void LogCompat(string message)
		{
			Debug.WriteLine($"[Compat] {message}");
			try
			{
				var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FastFluentFilesFolders");
				Directory.CreateDirectory(dir);
				File.AppendAllText(Path.Combine(dir, "compat.log"), $"{DateTime.Now:HH:mm:ss.fff} {message}\r\n");
			}
			catch { }
		}


		private static readonly HashSet<string> KnownCompatFlags = new(StringComparer.OrdinalIgnoreCase)
		{
			"VISTARTM", "WIN7RTM", "WIN8RTM", "WINXPSP3",
			"RUNASADMIN", "256COLOR", "640X480",
			"DISABLEDXMAXIMIZEDWINDOWEDMODE", "HIGHDPIAWARE",
			"PERPROCESSSYSTEMDPIFORCEON", "REGISTERAPPRESTART", "REGISTERINGAPPS", "ICM"
		};

		private static readonly HashSet<string> CompatModeFlags = new(StringComparer.OrdinalIgnoreCase)
		{
			"VISTARTM", "WIN7RTM", "WIN8RTM", "WINXPSP3"
		};

		private readonly FileSystemNodeViewModel _item;
		private HashSet<string>? _existingCompatFlags;
		private string? _highDpiMode;

		public string TroubleText => App.ML.Get("PropertiesCompatTroubleText");
		public string TroubleButtonText => App.ML.Get("PropertiesCompatTrouble");
		public string ManualLinkText => App.ML.Get("PropertiesCompatManualLink");
		public string CompatModeHeader => App.ML.Get("PropertiesCompatibilityMode");
		public string CompatEnabledLabel => App.ML.Get("PropertiesCompatibilityEnabled");
		public string SettingsHeader => App.ML.Get("PropertiesCompatSettings");
		public string SimplifiedColorsLabel => App.ML.Get("PropertiesSimplifiedColors");
		public string ColorModeDescription => App.ML.Get("PropertiesColor8bit");
		public string Run640x480Label => App.ML.Get("PropertiesRun640x480");
		public string DisableFullscreenOptimizationsLabel => App.ML.Get("PropertiesDisableFullscreenOptimizations");
		public string RunAsAdminLabel => App.ML.Get("PropertiesRunAsAdmin");
		public string RegisterForRestartLabel => App.ML.Get("PropertiesRegisterForRestart");
		public string LegacyIccLabel => App.ML.Get("PropertiesLegacyIcc");
		public string ChangeHighDpiLabel => App.ML.Get("PropertiesChangeHighDpi");
		public string ChangeAllUsersLabel => App.ML.Get("PropertiesChangeAllUsers");

		[ObservableProperty] public partial bool CompatModeEnabled { get; set; }
		[ObservableProperty] public partial bool SimplifiedColorsEnabled { get; set; }
		[ObservableProperty] public partial bool Run640x480 { get; set; }
		[ObservableProperty] public partial bool DisableFullscreenOptimizations { get; set; }
		[ObservableProperty] public partial bool RunAsAdmin { get; set; }
		[ObservableProperty] public partial bool RegisterForRestart { get; set; }
		[ObservableProperty] public partial bool LegacyIcc { get; set; }

		public IReadOnlyList<KeyValueOption> CompatModes { get; }
		public IReadOnlyList<KeyValueOption> ColorModes { get; }

		[ObservableProperty] public partial int SelectedCompatModeIndex { get; set; }
		[ObservableProperty] public partial int SelectedColorModeIndex { get; set; }

		public PropertiesCompatibilityViewModel(FileSystemNodeViewModel item)
		{
			_item = item;
			CompatModes = new List<KeyValueOption>
			{
				new(App.ML.Get("PropertiesCompatibilityNoMode"), ""),
				new(App.ML.Get("PropertiesCompatibilityVista"), "VISTARTM"),
				new(App.ML.Get("PropertiesCompatibilityWin7"), "WIN7RTM"),
				new(App.ML.Get("PropertiesCompatibilityWin8"), "WIN8RTM"),
				new(App.ML.Get("PropertiesCompatibilityWinXpSp3"), "WINXPSP3")
			};
			ColorModes = new List<KeyValueOption> { new(App.ML.Get("PropertiesColor8bit"), "256COLOR") };
			LoadState();
		}

		/// <summary>用于高 DPI 对话框的当前值。</summary>
		public string? HighDpiMode => _highDpiMode;
		/// <summary>高 DPI 对话框选择后回写。</summary>
		public void SetHighDpiMode(string? mode) => _highDpiMode = mode;

		partial void OnCompatModeEnabledChanged(bool value) => OnPropertyChanged(nameof(IsCompatModeComboEnabled));
		partial void OnSimplifiedColorsEnabledChanged(bool value) => OnPropertyChanged(nameof(IsColorModeComboEnabled));
		public bool IsCompatModeComboEnabled => CompatModeEnabled;
		public bool IsColorModeComboEnabled => SimplifiedColorsEnabled;

		private void LoadState()
		{
			try
			{
				var flags = ReadCompatibilityFlags();
				_existingCompatFlags = new HashSet<string>(flags, StringComparer.OrdinalIgnoreCase);

				var modeTag = CompatModes.FirstOrDefault(m =>
					m.Tag.Length > 0 && CompatModeFlags.Contains(m.Tag) && _existingCompatFlags.Contains(m.Tag))?.Tag ?? string.Empty;
				SelectedCompatModeIndex = CompatModes.ToList().FindIndex(m => m.Tag == modeTag && m.Tag.Length > 0);
				if (SelectedCompatModeIndex < 0) SelectedCompatModeIndex = 0;
				CompatModeEnabled = _existingCompatFlags.Any(f => CompatModeFlags.Contains(f));

				SimplifiedColorsEnabled = _existingCompatFlags.Contains("256COLOR");
				RunAsAdmin = _existingCompatFlags.Contains("RUNASADMIN");
				Run640x480 = _existingCompatFlags.Contains("640X480");
				DisableFullscreenOptimizations = _existingCompatFlags.Contains("DISABLEDXMAXIMIZEDWINDOWEDMODE");
				RegisterForRestart = _existingCompatFlags.Contains("REGISTERAPPRESTART") ||
				                     _existingCompatFlags.Contains("REGISTERINGAPPS");
				LegacyIcc = _existingCompatFlags.Contains("ICM");

				_highDpiMode = _existingCompatFlags.Contains("PERPROCESSSYSTEMDPIFORCEON")
					? "PERPROCESSSYSTEMDPIFORCEON"
					: _existingCompatFlags.Contains("HIGHDPIAWARE") ? "HIGHDPIAWARE" : null;
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[Properties] Load compatibility state failed: {ex.Message}");
			}
		}

		public string? LastError { get; private set; }

		/// <summary>当前是否运行在 MSIX 打包模式（打包模式无法写系统共享注册表，资源管理器不可见）。</summary>
		public bool IsPackagedMode => IsPackaged();
		public string PackagedWarning => App.ML.Get("PropertiesCompatPackagedWarning");

		/// <summary>写入兼容性设置；成功返回 true，失败填充 LastError。</summary>
		public bool Apply()
		{
			LastError = null;
			LogCompat($"Apply() called for {_item.FullPath}");
			LogCompat($"state: Mode={CompatModeEnabled}(idx {SelectedCompatModeIndex}) Color={SimplifiedColorsEnabled} Admin={RunAsAdmin} 640x480={Run640x480} FullscreenOpt={DisableFullscreenOptimizations} Restart={RegisterForRestart} ICC={LegacyIcc} HighDpi={_highDpiMode}");
			try
			{
				var value = BuildValue();
				LogCompat($"BuildValue => {(value == null ? "(null)" : value)}");
				if (value == null)
				{
					LogCompat("nothing to write, removing any existing values");
					if (IsPackaged()) DeleteRealRegistryViaReg();
					else RemoveFromAllViews();
					return true;
				}

				// 同时写入 64 位与 32 位视图（打包模式时写包私有容器，FFFF 自身可读回；未打包时写真实注册表，资源管理器可见）
				WriteView(RegistryView.Registry64, value);
				WriteView(RegistryView.Registry32, value);

				// 校验回读：写入失败时立即报错而不是静默失败
				var verify = ReadCompatibilityFlags();
				LogCompat($"verify read-back: {(verify.Count == 0 ? "(empty)" : string.Join(" ", verify))}");
				if (verify.Count == 0)
				{
					LastError = App.ML.Get("PropertiesCompatApplyVerifyFailed");
					Debug.WriteLine($"[Properties] Compatibility write verification failed for {_item.FullPath}");
					return false;
				}
				return true;
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[Properties] Apply compatibility settings failed: {ex.Message}");
				LastError = ex.Message;
				return false;
			}
		}

		private string? BuildValue()
		{
			var flags = new List<string>();
			if (_existingCompatFlags != null)
				flags.AddRange(_existingCompatFlags.Where(f => !KnownCompatFlags.Contains(f) && f != "~"));

			var modeTag = SelectedCompatModeIndex >= 0 && SelectedCompatModeIndex < CompatModes.Count
				? CompatModes[SelectedCompatModeIndex].Tag
				: string.Empty;
			if (CompatModeEnabled && !string.IsNullOrWhiteSpace(modeTag))
				flags.Add(modeTag);
			if (RunAsAdmin) flags.Add("RUNASADMIN");
			if (SimplifiedColorsEnabled) flags.Add("256COLOR");
			if (Run640x480) flags.Add("640X480");
			if (DisableFullscreenOptimizations) flags.Add("DISABLEDXMAXIMIZEDWINDOWEDMODE");
			if (_highDpiMode != null) flags.Add(_highDpiMode);
			if (RegisterForRestart) flags.Add("REGISTERAPPRESTART");
			if (LegacyIcc) flags.Add("ICM");

			return flags.Count > 0 ? "~ " + string.Join(' ', flags) : null;
		}

		private void WriteView(RegistryView view, string value)
		{
			RegistryKey? baseKey = null;
			RegistryKey? key = null;
			try
			{
				LogCompat($"OpenBaseKey({view}) for {_item.FullPath}");
				baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
				LogCompat($"CreateSubKey({CompatLayersKeyPath})");
				key = baseKey.CreateSubKey(CompatLayersKeyPath);
				LogCompat($"SetValue({_item.FullPath}) = '{value}'");
				key?.SetValue(_item.FullPath, value, RegistryValueKind.String);
				LogCompat($"SetValue done ({view})");
			}
			finally
			{
				key?.Dispose();
				baseKey?.Dispose();
				LogCompat($"WriteView exit ({view})");
			}
		}

		private void RemoveFromAllViews()
		{
			foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
			{
				try
				{
					using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
					using var key = baseKey.OpenSubKey(CompatLayersKeyPath, writable: true);
					key?.DeleteValue(_item.FullPath, throwOnMissingValue: false);
				}
				catch { }
			}
		}

		private List<string> ReadCompatibilityFlags()
		{
			var result = new List<string>();
			foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
			{
				try
				{
					using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
					using var key = baseKey.OpenSubKey(CompatLayersKeyPath);
					if (key?.GetValue(_item.FullPath) is string raw)
					{
						result.AddRange(raw.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
						break; // 找到即停（64 位优先）
					}
				}
				catch { }
			}
			return result;
		}

		/// <summary>当前进程是否运行在 MSIX 打包环境中（打包应用写 HKCU\Software 会被重定向到包私有容器）。</summary>
		private static bool IsPackaged()
		{
			try { _ = Windows.ApplicationModel.Package.Current; return true; }
			catch { return false; }
		}

		private static string Quote(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";

		/// <summary>通过 reg.exe（shell 启动，不带包身份）写入真实注册表。返回是否成功（退出码 0）。</summary>
		private bool WriteRealRegistryViaReg(string value)
		{
			return RunReg($"add {Quote(RegLayersKey)} /v {Quote(_item.FullPath)} /t REG_SZ /d {Quote(value)} /f");
		}

		private void DeleteRealRegistryViaReg()
		{
			RunReg($"delete {Quote(RegLayersKey)} /v {Quote(_item.FullPath)} /f");
		}

		/// <summary>通过 reg.exe 查询真实注册表中的值（cmd 重定向到临时文件，保持 shell 启动不带包身份）。</summary>
		private string? ReadRealRegistryViaReg()
		{
			var tmp = Path.GetTempFileName();
			try
			{
				var psi = new ProcessStartInfo("cmd.exe",
					$"/c \"\"{Quote(RegExePath)}\" query {Quote(RegLayersKey)} /v {Quote(_item.FullPath)}\" > \"{tmp}\" 2>&1")
				{
					UseShellExecute = true,
					WindowStyle = ProcessWindowStyle.Hidden,
					CreateNoWindow = true
				};
				using var p = Process.Start(psi);
				p?.WaitForExit(10000);
				if (File.Exists(tmp))
				{
					foreach (var line in File.ReadAllLines(tmp))
					{
						var idx = line.IndexOf("REG_SZ", StringComparison.OrdinalIgnoreCase);
						if (idx >= 0) return line[(idx + 6)..].Trim();
					}
				}
			}
			catch (Exception ex)
			{
				LogCompat($"reg.exe query failed: {ex.Message}");
			}
			finally
			{
				try { File.Delete(tmp); } catch { }
			}
			return null;
		}

		private static bool RunReg(string arguments)
		{
			try
			{
				var psi = new ProcessStartInfo(RegExePath, arguments)
				{
					UseShellExecute = true,
					WindowStyle = ProcessWindowStyle.Hidden
				};
				using var p = Process.Start(psi);
				p?.WaitForExit(10000);
				var ok = p?.ExitCode == 0;
				LogCompat($"reg.exe => exit {p?.ExitCode} ({(ok ? "ok" : "FAILED")}) : {arguments}");
				return ok;
			}
			catch (Exception ex)
			{
				LogCompat($"reg.exe failed to start: {ex.Message}");
				return false;
			}
		}
	}
}

	/// <summary>下拉选项（显示文本 + 隐藏 Tag）。</summary>
	public sealed class KeyValueOption
	{
		public string Text { get; }
		public string Tag { get; }

		public KeyValueOption(string text, string tag)
		{
			Text = text;
			Tag = tag;
		}

		/// <summary>WinUI 3 ComboBox 折叠选择框不使用 ItemTemplate 时回退到 ToString()。</summary>
		public override string ToString() => Text;
	}