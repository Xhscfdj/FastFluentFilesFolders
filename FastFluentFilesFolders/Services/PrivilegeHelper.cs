using System;
using System.Runtime.InteropServices;

namespace FastFluentFilesFolders.Services
{
	/// <summary>启用当前进程令牌中的特权，用于更改文件所有者（与资源管理器一致）。</summary>
	internal static class PrivilegeHelper
	{
		private const uint TokenAdjustPrivileges = 0x0020;
		private const uint TokenQuery = 0x0008;
		private const uint SePrivilegeEnabled = 0x00000002;

		[StructLayout(LayoutKind.Sequential)]
		private struct Luid
		{
			public uint LowPart;
			public int HighPart;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct TokenPrivileges
		{
			public uint PrivilegeCount;
			public Luid Luid;
			public uint Attributes;
		}

		[DllImport("kernel32.dll")]
		private static extern IntPtr GetCurrentProcess();

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool CloseHandle(IntPtr handle);

		[DllImport("advapi32.dll", SetLastError = true)]
		private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

		[DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern bool LookupPrivilegeValue(string? systemName, string name, out Luid luid);

		[DllImport("advapi32.dll", SetLastError = true)]
		private static extern bool AdjustTokenPrivileges(
			IntPtr tokenHandle,
			bool disableAllPrivileges,
			ref TokenPrivileges newState,
			uint bufferLength,
			IntPtr previousState,
			IntPtr returnLength);

		/// <summary>尝试启用指定特权；当前令牌未持有该特权时返回 false。</summary>
		public static bool EnablePrivilege(string name)
		{
			IntPtr token = IntPtr.Zero;
			try
			{
				if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out token))
					return false;
				if (!LookupPrivilegeValue(null, name, out var luid))
					return false;

				var state = new TokenPrivileges
				{
					PrivilegeCount = 1,
					Luid = luid,
					Attributes = SePrivilegeEnabled
				};
				if (!AdjustTokenPrivileges(token, false, ref state, 0, IntPtr.Zero, IntPtr.Zero))
					return false;

				// ERROR_NOT_ALL_ASSIGNED(1300) 表示令牌中并未持有该特权。
				return Marshal.GetLastWin32Error() == 0;
			}
			catch
			{
				return false;
			}
			finally
			{
				if (token != IntPtr.Zero)
					CloseHandle(token);
			}
		}

		/// <summary>启用取得所有权与还原所需的特权。非管理员时静默失败。</summary>
		public static void EnableOwnershipPrivileges()
		{
			EnablePrivilege("SeTakeOwnershipPrivilege");
			EnablePrivilege("SeRestorePrivilege");
		}
	}
}
