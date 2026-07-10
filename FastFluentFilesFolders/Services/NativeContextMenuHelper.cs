using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace FastFluentFilesFolders.Services
{
	public class ShellContextMenuItem
	{
		public string Label { get; set; } = "";
		public bool IsSeparator { get; set; }
		public bool IsEnabled { get; set; } = true;
		public int CommandId { get; set; }
	}

	public static class NativeContextMenuHelper
	{
		private const uint CMF_NORMAL = 0x00000000;
		private const uint CMF_EXPLORE = 0x00000004;

		private const uint MIIM_FTYPE = 0x00000100;
		private const uint MIIM_ID = 0x00000002;
		private const uint MIIM_STATE = 0x00000001;
		private const uint MIIM_STRING = 0x00000040;

		private const uint MFT_SEPARATOR = 0x00000800;
		private const uint MFS_DISABLED = 0x00000003;
		private const uint MFS_GRAYED = 0x00000003;

		private static readonly Guid IID_IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
		private static readonly Guid IID_IContextMenu = new("000214E4-0000-0000-C000-000000000046");
		private static readonly Guid BHID_SFUIObject = new("3981e225-f559-11d3-8e3a-00c04f6837d5");

		[ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
		[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		private interface IShellItem
		{
			[PreserveSig] int BindToHandler(IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid bhid, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);
			[PreserveSig] int GetParent(out IShellItem ppsi);
			[PreserveSig] int GetDisplayName(uint sigdnName, out IntPtr ppszName);
			[PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
			[PreserveSig] int Compare(IShellItem psi, uint hint, out int piOrder);
		}

		[ComImport, Guid("000214E4-0000-0000-C000-000000000046")]
		[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		private interface IContextMenu
		{
			[PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
			[PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFO pici);
			[PreserveSig] int GetCommandString(IntPtr idCmd, uint uType, IntPtr pwReserved, IntPtr pszName, uint cchMax);
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct CMINVOKECOMMANDINFO
		{
			public uint cbSize;
			public uint fMask;
			public IntPtr hwnd;
			public IntPtr lpVerb;
			[MarshalAs(UnmanagedType.LPWStr)] public string lpParameters;
			[MarshalAs(UnmanagedType.LPWStr)] public string lpDirectory;
			public int nShow;
			public uint dwHotKey;
			public IntPtr hIcon;
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct MENUITEMINFO
		{
			public uint cbSize;
			public uint fMask;
			public uint fType;
			public uint fState;
			public uint wID;
			public IntPtr hSubMenu;
			public IntPtr hbmpChecked;
			public IntPtr hbmpUnchecked;
			public IntPtr dwItemData;
			public IntPtr dwTypeData;
			public uint cch;
			public IntPtr hbmpItem;
		}

		[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
		private static extern int SHCreateItemFromParsingName([MarshalAs(UnmanagedType.LPWStr)] string pszPath, IntPtr pbc, ref Guid riid, out IShellItem ppv);

		[DllImport("user32.dll")]
		private static extern IntPtr CreatePopupMenu();

		[DllImport("user32.dll")]
		private static extern bool DestroyMenu(IntPtr hMenu);

		[DllImport("user32.dll")]
		private static extern int GetMenuItemCount(IntPtr hMenu);

		[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern bool GetMenuItemInfo(IntPtr hMenu, uint uItem, bool fByPosition, ref MENUITEMINFO lpmii);

		public static List<ShellContextMenuItem> BuildMenuItems(string path, IntPtr hwnd)
		{
			var items = new List<ShellContextMenuItem>();
			if (string.IsNullOrEmpty(path))
				return items;

			IShellItem? shellItem = null;
			IContextMenu? contextMenu = null;

			try
			{
				Guid iidShellItem = IID_IShellItem;
				int hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iidShellItem, out shellItem);
				if (hr != 0 || shellItem == null)
					return items;

				Guid iidCtxMenu = IID_IContextMenu;
				Guid bhid = BHID_SFUIObject;
				hr = shellItem.BindToHandler(IntPtr.Zero, bhid, iidCtxMenu, out IntPtr ppv);
				if (hr != 0 || ppv == IntPtr.Zero)
					return items;

				contextMenu = (IContextMenu)Marshal.GetObjectForIUnknown(ppv);

				IntPtr hMenu = CreatePopupMenu();
				if (hMenu == IntPtr.Zero)
					return items;

				try
				{
					hr = contextMenu.QueryContextMenu(hMenu, 0, 1, 0x7FFF, CMF_NORMAL | CMF_EXPLORE);
					if (hr < 0)
						return items;

					int count = GetMenuItemCount(hMenu);
					for (uint i = 0; i < count; i++)
					{
						var mii = new MENUITEMINFO();
						mii.cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>();
						mii.fMask = MIIM_FTYPE | MIIM_ID | MIIM_STATE;
						mii.dwTypeData = IntPtr.Zero;
						mii.cch = 0;

						if (!GetMenuItemInfo(hMenu, i, true, ref mii))
							continue;

						if ((mii.fType & MFT_SEPARATOR) != 0)
						{
							items.Add(new ShellContextMenuItem { IsSeparator = true });
							continue;
						}

						uint itemCmdId = mii.wID;
						bool isDisabled = (mii.fState & MFS_GRAYED) == MFS_GRAYED || (mii.fState & MFS_DISABLED) == MFS_DISABLED;

						string label = "";
						IntPtr strBuffer = Marshal.AllocHGlobal(512);
						try
						{
							mii.cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>();
							mii.fMask = MIIM_STRING;
							mii.dwTypeData = strBuffer;
							mii.cch = 256;

							if (GetMenuItemInfo(hMenu, i, true, ref mii))
								label = Marshal.PtrToStringUni(strBuffer) ?? "";
						}
						finally
						{
							Marshal.FreeHGlobal(strBuffer);
						}

						items.Add(new ShellContextMenuItem
						{
							Label = label,
							IsSeparator = false,
							IsEnabled = !isDisabled,
							CommandId = (int)itemCmdId
						});
					}
				}
				finally
				{
					DestroyMenu(hMenu);
				}
			}
			finally
			{
				if (contextMenu != null)
					Marshal.ReleaseComObject(contextMenu);
				if (shellItem != null)
					Marshal.ReleaseComObject(shellItem);
			}

			return items;
		}

		public static void InvokeItem(string path, int commandId, IntPtr hwnd)
		{
			if (string.IsNullOrEmpty(path)) return;

			IShellItem? shellItem = null;
			IContextMenu? contextMenu = null;

			try
			{
				Guid iidShellItem = IID_IShellItem;
				int hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iidShellItem, out shellItem);
				if (hr != 0 || shellItem == null) return;

				Guid iidCtxMenu = IID_IContextMenu;
				Guid bhid = BHID_SFUIObject;
				hr = shellItem.BindToHandler(IntPtr.Zero, bhid, iidCtxMenu, out IntPtr ppv);
				if (hr != 0 || ppv == IntPtr.Zero) return;

				contextMenu = (IContextMenu)Marshal.GetObjectForIUnknown(ppv);

				var info = new CMINVOKECOMMANDINFO
				{
					cbSize = (uint)Marshal.SizeOf<CMINVOKECOMMANDINFO>(),
					fMask = 0,
					hwnd = hwnd,
					lpVerb = (IntPtr)(commandId - 1),
					lpParameters = null!,
					lpDirectory = null!,
					nShow = 1
				};
				contextMenu.InvokeCommand(ref info);
			}
			finally
			{
				if (contextMenu != null)
					Marshal.ReleaseComObject(contextMenu);
				if (shellItem != null)
					Marshal.ReleaseComObject(shellItem);
			}
		}
	}
}
