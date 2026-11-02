using FastFluentFilesFolders.ViewModels;
using NetSparkleUpdater;
using NetSparkleUpdater.AssemblyAccessors;
using NetSparkleUpdater.Configurations;
using NetSparkleUpdater.Interfaces;
using NetSparkleUpdater.SignatureVerifiers;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace FastFluentFilesFolders.Services
{
	/// <summary>
	/// 自动更新服务：封装 NetSparkle 的“引擎”部分（检查 / 下载 / 安装）。
	/// 这里刻意不设置 UIFactory —— NetSparkle 自带的界面是 WinForms/WPF 的，在 WinUI 3 里用不了；
	/// 所有界面都由 <see cref="Views.UpdateDialog"/> 自己绘制。
	/// </summary>
	public class AutoUpdateService
	{
		public string PubKey = "EjYqgKVoBny7s0TONIzq/P5P79bn85ybLw2g3hIdxVc=";
		public SparkleUpdater Sparkle { get; set; }
		public string AppcastUrl = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
			? "https://github.com/Xhscfdj/FastFluentFilesFolders/releases/latest/download/appcast-arm64.xml"
			: "https://github.com/Xhscfdj/FastFluentFilesFolders/releases/latest/download/appcast-x64.xml";

		/// <summary>当前已安装版本（与“关于”页显示的一致）。</summary>
		public string CurrentVersion => Configs.Version;

		/// <summary>
		/// NetSparkle 安装更新时会先启动一个“等待宿主进程退出”的脚本，然后要求宿主程序退出；
		/// 没有 UIFactory 时必须自己处理该事件，否则安装程序不会开始运行。
		/// </summary>
		public event EventHandler? CloseRequested;

		public AutoUpdateService()
		{
			Sparkle = new SparkleUpdater
			(
				AppcastUrl,
				new Ed25519Checker(NetSparkleUpdater.Enums.SecurityMode.Strict, PubKey)
			)
			{
				RelaunchAfterUpdate = true,
				CheckServerFileName = true,
				// 没有 UIFactory（不支持系统通知），关掉免得走空实现
				UseNotificationToast = false,
			};
			ApplyInstalledVersion();
			Sparkle.LogWriter = new SparkleFileLogger();
			Sparkle.CloseApplication += OnCloseApplication;
			Sparkle.StartLoop(true);
		}

		// ======================= 检查 / 下载 / 安装 =======================

		/// <summary>
		/// 检查更新，不会弹出 NetSparkle 自带界面（UI 由调用方负责）。
		/// 用户手动点“检查更新”时 ignoreSkippedVersions 传 true，跳过过的版本也照样提示。
		/// </summary>
		public Task<UpdateInfo> CheckForUpdatesAsync(bool ignoreSkippedVersions = true)
			=> Sparkle.CheckForUpdatesQuietly(ignoreSkippedVersions);

		/// <summary>
		/// 开始下载指定的更新包。进度 / 完成 / 失败通过 <see cref="SparkleUpdater"/> 上的
		/// DownloadMadeProgress、DownloadFinished、DownloadHadError、DownloadedFileIsCorrupt 事件返回。
		/// </summary>
		public Task DownloadUpdateAsync(AppCastItem item) => Sparkle.InitAndBeginDownload(item);

		/// <summary>安装已下载的更新包。Sparkle 会先拉起安装脚本，然后触发 <see cref="CloseRequested"/>。</summary>
		public Task InstallUpdateAsync(AppCastItem item, string? downloadPath)
			=> Sparkle.InstallUpdate(item, downloadPath);

		/// <summary>跳过某个版本：后台自动检查时不再提示该版本（手动检查仍然会提示）。</summary>
		public void SkipVersion(string? version)
		{
			if (string.IsNullOrWhiteSpace(version)) return;
			try
			{
				Sparkle.Configuration.SetVersionToSkip(version!);
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[AutoUpdate] 跳过版本失败: {ex.Message}");
			}
		}

		/// <summary>取消正在进行的下载。</summary>
		public void CancelDownload() => Sparkle.CancelFileDownload();

		private void OnCloseApplication() => CloseRequested?.Invoke(this, EventArgs.Empty);

		// ======================= 版本号 =======================

		/// <summary>
		/// NetSparkle 默认拿 exe 的 ProductVersion 当作“已安装版本”。本工程没有在 csproj 里写
		/// &lt;Version&gt;，于是会读到 “1.0.0+commit”；这样即使更新成功，也仍然会被判定为“有新版本”，
		/// 而且和“关于”页显示的版本号对不上。这里统一改成用 Configs.Version。
		/// 配置存放路径（注册表）由 Company/Product 决定，包装后仍然是同一个键，跳过版本等记录不受影响。
		/// </summary>
		private void ApplyInstalledVersion()
		{
			try
			{
				var inner = new AssemblyDiagnosticsAccessor(null);
				Sparkle.Configuration = new RegistryConfiguration(
					new FixedVersionAssemblyAccessor(inner, Configs.Version));
			}
			catch (Exception ex)
			{
				// 读不到程序集信息时保留 NetSparkle 默认配置，不影响后台自动检查
				Debug.WriteLine($"[AutoUpdate] 无法覆盖已安装版本号: {ex.Message}");
			}
		}

		/// <summary>
		/// 把 NetSparkle 的内部日志写到 %LocalAppData%\FastFluentFilesFolders\update.log。
		/// 默认它只往控制台写，WinUI 程序看不到；有了日志，像“appcast 签名不匹配”这类
		/// 检查更新失败的原因可以直接从日志里读到。
		/// </summary>
		private sealed class SparkleFileLogger : ILogger
		{
			private const long MaxLogBytes = 512 * 1024;
			private readonly object _lock = new();
			private readonly string _logPath;

			public SparkleFileLogger()
			{
				_logPath = Path.Combine(
					Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
					"FastFluentFilesFolders", "update.log");
				try
				{
					// 别让日志无限增长
					if (File.Exists(_logPath) && new FileInfo(_logPath).Length > MaxLogBytes)
						File.Delete(_logPath);
				}
				catch { }
			}

			public void PrintMessage(string message, params object?[]? arguments)
			{
				try
				{
					var text = arguments is { Length: > 0 }
						? string.Format(message, arguments)
						: message;
					lock (_lock)
					{
						Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
						File.AppendAllText(_logPath,
							DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + text + Environment.NewLine);
					}
				}
				catch
				{
					// 日志失败不能影响更新流程
				}
			}
		}

		/// <summary>只把 AssemblyVersion 换成 App 内的版本号，其余沿用 FileVersionInfo 的结果。</summary>
		private sealed class FixedVersionAssemblyAccessor : IAssemblyAccessor
		{
			private readonly IAssemblyAccessor _inner;
			private readonly string _version;

			public FixedVersionAssemblyAccessor(IAssemblyAccessor inner, string version)
			{
				_inner = inner;
				_version = version;
			}

			public string AssemblyVersion => _version;
			public string AssemblyTitle => _inner.AssemblyTitle;
			public string AssemblyDescription => _inner.AssemblyDescription;
			public string AssemblyCompany => _inner.AssemblyCompany;
			public string AssemblyProduct => _inner.AssemblyProduct;
			public string AssemblyCopyright => _inner.AssemblyCopyright;
		}
	}
}
