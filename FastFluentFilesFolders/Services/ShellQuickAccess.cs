using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace FastFluentFilesFolders.Services
{
	/// <summary>
	/// 快速访问（Quick Access）固定项的**AOT 安全**读写。
	/// <para>
	/// Native AOT 会关闭 .NET 的「内置 COM」支持：RCW/CCW 封送、<c>Type.GetTypeFromProgID</c>、
	/// <c>dynamic</c>、<c>[ComImport]</c> 全部不可用，但 COM 运行时本身仍然在。所以这里不用任何
	/// 托管↔COM 封送：先用 <c>SHParseDisplayName</c> / <c>SHBindToParent</c> 这类纯指针 P/Invoke
	/// 拿到原始接口指针，再按 vtable 下标手工调用 <c>IShellFolder</c> /
	/// <c>IEnumIDList</c> / <c>IContextMenu</c>。
	/// </para>
	/// <para>
	/// 枚举：桌面 IShellFolder → ParseDisplayName("shell:::{679f85cb-…}") → BindToObject
	/// → EnumObjects → Next → ILCombine + SHGetNameFromIDList 取真实路径。<br/>
	/// 固定/取消固定：SHBindToParent → GetUIObjectOf(IContextMenu) → QueryContextMenu →
	/// 用 GetCommandString(GCS_VERBW) 找到 canonical verb（pintohome / unpinfromhome，
	/// 与系统语言无关）→ InvokeCommand。
	/// </para>
	/// Shell 扩展对线程套间敏感，所以真正的调用都放在 STA 线程上跑（带超时，避免 shell 卡死拖住 UI）。
	/// </summary>
	internal static unsafe class ShellQuickAccess
	{
		private const string QuickAccessClsidPath = "shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}";
		private const string PinVerb = "pintohome";
		private const string UnpinVerb = "unpinfromhome";

		private const uint SHCONTF_FOLDERS = 0x20;
		private const uint SIGDN_FILESYSPATH = 0x80058000;
		private const uint SIGDN_DESKTOPABSOLUTEPARSING = 0x80028000;
		private const uint CMF_NORMAL = 0x0;
		private const uint GCS_VERBW = 0x4;
		private const uint CMIC_MASK_FLAG_NO_UI = 0x400;
		private const int SW_SHOWNORMAL = 1;

		// —— vtable 下标（IUnknown 占 0 QueryInterface / 1 AddRef / 2 Release）——
		private const int VT_RELEASE = 2;
		private const int SF_PARSE_DISPLAY_NAME = 3;
		private const int SF_ENUM_OBJECTS = 4;
		private const int SF_BIND_TO_OBJECT = 5;
		private const int SF_GET_UI_OBJECT_OF = 10;
		private const int ENUM_NEXT = 3;
		private const int CM_QUERY_CONTEXT_MENU = 3;
		private const int CM_INVOKE_COMMAND = 4;
		private const int CM_GET_COMMAND_STRING = 5;

		private static readonly Guid IidShellFolder = new("000214E6-0000-0000-C000-000000000046");
		private static readonly Guid IidContextMenu = new("000214E4-0000-0000-C000-000000000046");

		/// <summary>当前系统上这条路径是否可用（Windows 10 1809+）。</summary>
		public static bool IsSupported { get; } = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763);

		#region P/Invoke

		[DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
		private static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

		[DllImport("shell32.dll")]
		private static extern IntPtr SHGetDesktopFolder(out IntPtr ppshf);

		[DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
		private static extern int SHBindToParent(IntPtr pidl, ref Guid riid, out IntPtr ppv, out IntPtr ppidlLast);

		[DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
		private static extern int SHGetNameFromIDList(IntPtr pidl, uint sigdnName, out IntPtr ppszName);

		[DllImport("shell32.dll")]
		private static extern IntPtr ILCombine(IntPtr pidl1, IntPtr pidl2);

		[DllImport("user32.dll")]
		private static extern IntPtr CreatePopupMenu();

		[DllImport("user32.dll")]
		private static extern bool DestroyMenu(IntPtr hMenu);

		[DllImport("ole32.dll")]
		private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

		[DllImport("ole32.dll")]
		private static extern void CoUninitialize();

		[DllImport("user32.dll")]
		private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

		private const uint PM_NOREMOVE = 0x0000;

		[StructLayout(LayoutKind.Sequential)]
		private struct MSG
		{
			public IntPtr hwnd;
			public uint message;
			public IntPtr wParam;
			public IntPtr lParam;
			public uint time;
			public int ptX;
			public int ptY;
		}

		private const uint COINIT_APARTMENTTHREADED = 0x2;

		[StructLayout(LayoutKind.Sequential)]
		private struct CMINVOKECOMMANDINFO
		{
			public int cbSize;
			public uint fMask;
			public IntPtr hwnd;
			public IntPtr lpVerb;
			public IntPtr lpParameters;
			public IntPtr lpDirectory;
			public int nShow;
			public uint dwHotKey;
			public IntPtr hIcon;
		}

		#endregion

		#region 公共入口

		/// <summary>
		/// 枚举「快速访问」里的文件夹（含用户固定的与系统自动收集的常用文件夹），返回真实文件系统路径。
		/// 失败/不支持时返回空列表，调用方自行兜底。
		/// </summary>
		public static List<string> EnumeratePinnedFolders()
		{
			var result = new List<string>();
			if (!IsSupported) return result;

			RunOnSta(() =>
			{
				IntPtr desktop = IntPtr.Zero, homePidl = IntPtr.Zero, homeFolder = IntPtr.Zero, enumList = IntPtr.Zero;
				try
				{
					if (SHGetDesktopFolder(out desktop) < 0 || desktop == IntPtr.Zero)
						return false;

					// IShellFolder::ParseDisplayName(hwnd, pbc, pszName, &chEaten, &pidl, &attrs)
					uint eaten = 0, attrs = 0;
					int hr;
					fixed (char* pszName = QuickAccessClsidPath)
					{
						var parse = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, IntPtr, uint*, IntPtr*, uint*, int>)
							((IntPtr*)*(IntPtr*)desktop)[SF_PARSE_DISPLAY_NAME];
						hr = parse(desktop, IntPtr.Zero, IntPtr.Zero, (IntPtr)pszName, &eaten, &homePidl, &attrs);
					}
					if (hr < 0 || homePidl == IntPtr.Zero)
						return false;

					// IShellFolder::BindToObject(pidl, pbc, riid, &ppv)
					var iidFolder = IidShellFolder;
					var bind = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, Guid*, IntPtr*, int>)
						((IntPtr*)*(IntPtr*)desktop)[SF_BIND_TO_OBJECT];
					hr = bind(desktop, homePidl, IntPtr.Zero, &iidFolder, &homeFolder);
					if (hr < 0 || homeFolder == IntPtr.Zero)
						return false;

					// IShellFolder::EnumObjects(hwnd, grfFlags, &ppenum)
					var enumObjects = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, IntPtr*, int>)
						((IntPtr*)*(IntPtr*)homeFolder)[SF_ENUM_OBJECTS];
					hr = enumObjects(homeFolder, IntPtr.Zero, SHCONTF_FOLDERS, &enumList);
					if (hr < 0 || enumList == IntPtr.Zero)
						return false;

					// IEnumIDList::Next(celt, &rgelt, &fetched)
					var next = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, uint*, int>)
						((IntPtr*)*(IntPtr*)enumList)[ENUM_NEXT];
					var guard = 0;
					while (guard++ < 4096)
					{
						IntPtr child = IntPtr.Zero;
						uint fetched = 0;
						hr = next(enumList, 1, &child, &fetched);
						if (hr != 0 || fetched == 0 || child == IntPtr.Zero)
							break;

						var absolute = ILCombine(homePidl, child);
						if (absolute != IntPtr.Zero)
						{
							var name = GetNameFromPidl(absolute);
							if (!string.IsNullOrEmpty(name)
								&& Directory.Exists(name)
								&& !result.Contains(name!, StringComparer.OrdinalIgnoreCase))
							{
								result.Add(name!);
							}
							Marshal.FreeCoTaskMem(absolute);
						}
						Marshal.FreeCoTaskMem(child);
					}
					return result.Count > 0;
				}
				catch (Exception ex)
				{
					Debug.WriteLine($"[ShellQuickAccess] 枚举失败: {ex.Message}");
					return false;
				}
				finally
				{
					Release(enumList);
					Release(homeFolder);
					Release(desktop);
					if (homePidl != IntPtr.Zero) Marshal.FreeCoTaskMem(homePidl);
				}
			}, "枚举快速访问", 15000);

			return result;
		}
		/// <summary>
		/// 把文件夹固定到 / 取消固定于系统「快速访问」。返回是否确实调用了对应的 Shell 动词。
		/// <para>固定：对文件系统项本身调 <c>pintohome</c>；
		/// 取消固定：动词挂在「快速访问命名空间里的那一项」上，所以先枚举该命名空间找到对应项再调
		/// <c>unpinfromhome</c>（与 PowerShell 里 <c>$qa.Items() | ? Path -eq … | InvokeVerb("unpinfromhome")</c> 等价）。</para>
		/// </summary>
		public static bool SetPinned(string folderPath, bool pin)
		{
			if (!IsSupported || string.IsNullOrWhiteSpace(folderPath)) return false;

			// 防挂死：对「已固定」的文件夹去查文件系统项的上下文菜单时，shell 里的
			// Pin/Unpin 处理程序会长时间阻塞（实测会吃满超时），所以固定前先确认还没固定。
			if (pin && EnumeratePinnedFolders().Any(
					p => string.Equals(p.TrimEnd('\\'), folderPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
				return true;

			var ok = false;
			RunOnSta(() =>
			{
				ok = pin
					? InvokeOnFileSystemItem(folderPath, PinVerb)
					: (InvokeOnQuickAccessItem(folderPath, UnpinVerb) || InvokeOnFileSystemItem(folderPath, UnpinVerb));
				return ok;
			}, pin ? "固定到快速访问" : "取消固定", 20000);

			return ok;
		}


		#endregion

		#region 内部工具

		/// <summary>在「文件系统项本身」上执行：SHParseDisplayName → SHBindToParent → (父IShellFolder, 子pidl)。</summary>
		private static bool WithFileSystemItem(string folderPath, Func<IntPtr, IntPtr, bool> body)
		{
			IntPtr pidl = IntPtr.Zero, parentFolder = IntPtr.Zero, childPidl = IntPtr.Zero;
			try
			{
				if (SHParseDisplayName(folderPath, IntPtr.Zero, out pidl, 0, out _) < 0 || pidl == IntPtr.Zero)
					return false;

				var iidFolder = IidShellFolder;
				if (SHBindToParent(pidl, ref iidFolder, out parentFolder, out childPidl) < 0
					|| parentFolder == IntPtr.Zero || childPidl == IntPtr.Zero)
					return false;

				var result = body(parentFolder, childPidl);
				Release(parentFolder);
				parentFolder = IntPtr.Zero;
				return result;
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[ShellQuickAccess] 打开项失败({folderPath}): {ex.Message}");
				return false;
			}
			finally
			{
				Release(parentFolder);
				if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl);
			}
		}

		/// <summary>
		/// 在「快速访问命名空间里与本路径对应的那一项」上执行：枚举 Home 命名空间，按路径匹配后
		/// 把 (Home IShellFolder, 该项的子 pidl) 交给 body。取消固定用的就是这条路。
		/// </summary>
		private static bool WithQuickAccessItem(string folderPath, Func<IntPtr, IntPtr, bool> body)
		{
			IntPtr desktop = IntPtr.Zero, homePidl = IntPtr.Zero, homeFolder = IntPtr.Zero, enumList = IntPtr.Zero;
			try
			{
				if (SHGetDesktopFolder(out desktop) < 0 || desktop == IntPtr.Zero)
					return false;

				uint eaten = 0, attrs = 0;
				int hr;
				fixed (char* pszName = QuickAccessClsidPath)
				{
					var parse = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, IntPtr, uint*, IntPtr*, uint*, int>)
						((IntPtr*)*(IntPtr*)desktop)[SF_PARSE_DISPLAY_NAME];
					hr = parse(desktop, IntPtr.Zero, IntPtr.Zero, (IntPtr)pszName, &eaten, &homePidl, &attrs);
				}
				if (hr < 0 || homePidl == IntPtr.Zero) return false;

				var iidFolder = IidShellFolder;
				var bind = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, Guid*, IntPtr*, int>)
					((IntPtr*)*(IntPtr*)desktop)[SF_BIND_TO_OBJECT];
				hr = bind(desktop, homePidl, IntPtr.Zero, &iidFolder, &homeFolder);
				if (hr < 0 || homeFolder == IntPtr.Zero) return false;

				var enumObjects = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, IntPtr*, int>)
					((IntPtr*)*(IntPtr*)homeFolder)[SF_ENUM_OBJECTS];
				hr = enumObjects(homeFolder, IntPtr.Zero, SHCONTF_FOLDERS, &enumList);
				if (hr < 0 || enumList == IntPtr.Zero) return false;

				var next = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, uint*, int>)
					((IntPtr*)*(IntPtr*)enumList)[ENUM_NEXT];
				var target = folderPath.TrimEnd('\\');
				var guard = 0;
				while (guard++ < 4096)
				{
					IntPtr child = IntPtr.Zero;
					uint fetched = 0;
					hr = next(enumList, 1, &child, &fetched);
					if (hr != 0 || fetched == 0 || child == IntPtr.Zero) break;

					var absolute = ILCombine(homePidl, child);
					var name = absolute == IntPtr.Zero ? null : GetNameFromPidl(absolute);
					if (absolute != IntPtr.Zero) Marshal.FreeCoTaskMem(absolute);

					if (!string.IsNullOrEmpty(name)
						&& string.Equals(name!.TrimEnd('\\'), target, StringComparison.OrdinalIgnoreCase))
					{
						var matched = body(homeFolder, child);
						Marshal.FreeCoTaskMem(child);
						return matched;
					}
					Marshal.FreeCoTaskMem(child);
				}
				return false;
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[ShellQuickAccess] 匹配快速访问项失败({folderPath}): {ex.Message}");
				return false;
			}
			finally
			{
				Release(enumList);
				Release(homeFolder);
				Release(desktop);
				if (homePidl != IntPtr.Zero) Marshal.FreeCoTaskMem(homePidl);
			}
		}

		private static bool InvokeOnFileSystemItem(string folderPath, string verb)
			=> WithFileSystemItem(folderPath, (folder, child) => InvokeVerb(folder, child, verb));

		private static bool InvokeOnQuickAccessItem(string folderPath, string verb)
			=> WithQuickAccessItem(folderPath, (folder, child) => InvokeVerb(folder, child, verb));

		/// <summary>
		/// 在给定 IShellFolder + 子 pidl 上执行指定的 canonical verb：
		/// GetUIObjectOf(IContextMenu) → QueryContextMenu → GetCommandString(GCS_VERBW) 定位 → InvokeCommand。
		/// </summary>
		private static bool InvokeVerb(IntPtr folder, IntPtr childPidl, string wantedVerb)
		{
			IntPtr ctxMenu = IntPtr.Zero, hMenu = IntPtr.Zero;
			try
			{
				var iidMenu = IidContextMenu;
				var children = stackalloc IntPtr[1];
				children[0] = childPidl;
				var getUi = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, IntPtr*, Guid*, IntPtr, IntPtr*, int>)
					((IntPtr*)*(IntPtr*)folder)[SF_GET_UI_OBJECT_OF];
				if (getUi(folder, IntPtr.Zero, 1, children, &iidMenu, IntPtr.Zero, &ctxMenu) < 0 || ctxMenu == IntPtr.Zero)
				{
					return false;
				}

				hMenu = CreatePopupMenu();
				if (hMenu == IntPtr.Zero) return false;
				var queryMenu = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, uint, int>)
					((IntPtr*)*(IntPtr*)ctxMenu)[CM_QUERY_CONTEXT_MENU];
				var added = queryMenu(ctxMenu, hMenu, 0, 1, 0x7FFF, CMF_NORMAL);
				if (added <= 0) return false;

				var getCommandString = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, IntPtr, IntPtr, uint, int>)
					((IntPtr*)*(IntPtr*)ctxMenu)[CM_GET_COMMAND_STRING];
				var foundOffset = -1;
				var buffer = stackalloc char[260];
				for (var i = 0; i < added && foundOffset < 0; i++)
				{
					buffer[0] = '\0';
					if (getCommandString(ctxMenu, (IntPtr)i, GCS_VERBW, IntPtr.Zero, (IntPtr)buffer, 260) < 0)
						continue;
					if (string.Equals(new string(buffer), wantedVerb, StringComparison.OrdinalIgnoreCase))
						foundOffset = i;
				}
				if (foundOffset < 0)
				{
					return false;
				}

				var info = new CMINVOKECOMMANDINFO
				{
					cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFO>(),
					fMask = CMIC_MASK_FLAG_NO_UI,
					hwnd = IntPtr.Zero,
					lpVerb = (IntPtr)foundOffset, // MAKEINTRESOURCE(offset)
					lpParameters = IntPtr.Zero,
					lpDirectory = IntPtr.Zero,
					nShow = SW_SHOWNORMAL,
					dwHotKey = 0,
					hIcon = IntPtr.Zero
				};
				var invoke = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)
					((IntPtr*)*(IntPtr*)ctxMenu)[CM_INVOKE_COMMAND];
				var invokeHr = invoke(ctxMenu, (IntPtr)(&info));
				return invokeHr >= 0;
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[ShellQuickAccess] 调用动词 {wantedVerb} 失败: {ex.Message}");
				return false;
			}
			finally
			{
				if (hMenu != IntPtr.Zero) DestroyMenu(hMenu);
				Release(ctxMenu);
			}
		}


		private static string? GetNameFromPidl(IntPtr pidl)
		{
			IntPtr psz;
			if (SHGetNameFromIDList(pidl, SIGDN_FILESYSPATH, out psz) >= 0 && psz != IntPtr.Zero)
			{
				try { return Marshal.PtrToStringUni(psz); }
				finally { Marshal.FreeCoTaskMem(psz); }
			}
			if (SHGetNameFromIDList(pidl, SIGDN_DESKTOPABSOLUTEPARSING, out psz) >= 0 && psz != IntPtr.Zero)
			{
				try { return Marshal.PtrToStringUni(psz); }
				finally { Marshal.FreeCoTaskMem(psz); }
			}
			return null;
		}

		private static void Release(IntPtr unknown)
		{
			if (unknown == IntPtr.Zero) return;
			try
			{
				var release = (delegate* unmanaged[Stdcall]<IntPtr, int>)((IntPtr*)*(IntPtr*)unknown)[VT_RELEASE];
				release(unknown);
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[ShellQuickAccess] Release 失败: {ex.Message}");
			}
		}

		/// <summary>Shell 扩展对线程套间敏感，统一放到 STA 线程执行并带超时。</summary>
		private static void RunOnSta(Func<bool> action, string label, int timeoutMs)
		{
			var thread = new Thread(() =>
			{
				// 关键：Native AOT 关闭内置 COM 后，CLR 不会再替我们初始化套间，
				// 而 Shell 的 IShellFolder / IContextMenu 必须在已初始化的 STA 上调用
				// （否则枚举可能侥幸能用，但 InvokeCommand 这类动词会卡死）。
				var hr = CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);
				try
				{
					// 关键：先给本线程建一个消息队列。Shell 扩展（尤其右键菜单处理程序）
					// 会在调用期间向调用线程做跨套间回调，没有消息队列会直接死等。
					PeekMessage(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE);
					action();
				}
				catch (Exception ex) { Debug.WriteLine($"[ShellQuickAccess] {label} 异常: {ex}"); }
				finally
				{
					if (hr >= 0) CoUninitialize();
				}
			})
			{
				IsBackground = true
			};
			thread.SetApartmentState(ApartmentState.STA);
			thread.Start();
			if (!thread.Join(TimeSpan.FromMilliseconds(timeoutMs)))
				Debug.WriteLine($"[ShellQuickAccess] {label} 超时({timeoutMs}ms)");
		}

		#endregion
	}
}