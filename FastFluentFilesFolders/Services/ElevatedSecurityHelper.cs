using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace FastFluentFilesFolders.Services
{
	/// <summary>一次需要管理员权限的 ACL / 所有者变更请求。</summary>
	internal sealed class SecurityChangeRequest
	{
		public string Path { get; init; } = string.Empty;
		public bool IsDirectory { get; init; }
		public string? Owner { get; init; }
		public IReadOnlyList<SecurityRuleChange> Rules { get; init; } = Array.Empty<SecurityRuleChange>();
	}

	/// <summary>单个主体的显式权限规则。</summary>
	internal sealed class SecurityRuleChange
	{
		public string Identity { get; init; } = string.Empty;
		public long Allow { get; init; }
		public long Deny { get; init; }
		public int Inheritance { get; init; }
		public int Propagation { get; init; }
	}

	/// <summary>通过 UAC 提升的 PowerShell 进程应用权限/所有者变更（不依赖自身可执行文件是否可提升）。</summary>
	internal static class ElevatedSecurityHelper
	{
		private const int ErrorCancelled = 1223;

		/// <summary>
		/// 以管理员身份应用变更。返回是否成功；<paramref name="cancelled"/> 表示用户取消了 UAC；
		/// <paramref name="error"/> 为失败详情（若可得）。
		/// </summary>
		public static bool TryApply(SecurityChangeRequest request, out bool cancelled, out string? error)
		{
			cancelled = false;
			error = null;
			var errorFile = Path.Combine(Path.GetTempPath(), "lrs-security-" + Guid.NewGuid().ToString("N") + ".txt");

			try
			{
				var payloadBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(BuildPayload(request, errorFile)));
				var script = BuildScript(payloadBase64);
				var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

				var startInfo = new ProcessStartInfo
				{
					FileName = "powershell.exe",
					Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
					UseShellExecute = true,
					Verb = "runas",
					WindowStyle = ProcessWindowStyle.Hidden,
					CreateNoWindow = true
				};

				using var process = Process.Start(startInfo);
				if (process == null)
				{
					error = "Unable to start the elevated process.";
					return false;
				}

				process.WaitForExit();

				if (File.Exists(errorFile))
				{
					try { error = File.ReadAllText(errorFile); } catch { }
				}

				if (process.ExitCode == 0)
					return true;

				error ??= $"Elevated process exited with code {process.ExitCode}.";
				Debug.WriteLine($"[ElevatedSecurity] Failed: {error}");
				return false;
			}
			catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
			{
				cancelled = true;
				return false;
			}
			catch (Exception ex)
			{
				error = ex.Message;
				Debug.WriteLine($"[ElevatedSecurity] Exception: {ex}");
				return false;
			}
			finally
			{
				try { if (File.Exists(errorFile)) File.Delete(errorFile); } catch { }
			}
		}

		private static string BuildPayload(SecurityChangeRequest request, string errorFile)
		{
			var sb = new StringBuilder();
			sb.Append('{');
			sb.Append("\"path\":").Append(ToJsonString(request.Path)).Append(',');
			sb.Append("\"isDirectory\":").Append(request.IsDirectory ? "true" : "false").Append(',');
			sb.Append("\"owner\":").Append(request.Owner == null ? "null" : ToJsonString(request.Owner)).Append(',');
			sb.Append("\"errorFile\":").Append(ToJsonString(errorFile)).Append(',');
			sb.Append("\"rules\":[");
			for (var i = 0; i < request.Rules.Count; i++)
			{
				if (i > 0)
					sb.Append(',');
				var rule = request.Rules[i];
				sb.Append('{');
				sb.Append("\"identity\":").Append(ToJsonString(rule.Identity)).Append(',');
				sb.Append("\"allow\":").Append(rule.Allow.ToString(CultureInfo.InvariantCulture)).Append(',');
				sb.Append("\"deny\":").Append(rule.Deny.ToString(CultureInfo.InvariantCulture)).Append(',');
				sb.Append("\"inheritance\":").Append(rule.Inheritance.ToString(CultureInfo.InvariantCulture)).Append(',');
				sb.Append("\"propagation\":").Append(rule.Propagation.ToString(CultureInfo.InvariantCulture));
				sb.Append('}');
			}
			sb.Append("]}");
			return sb.ToString();
		}

		private static string ToJsonString(string value)
		{
			var sb = new StringBuilder(value.Length + 2);
			sb.Append('"');
			foreach (var c in value)
			{
				switch (c)
				{
					case '"': sb.Append("\\\""); break;
					case '\\': sb.Append("\\\\"); break;
					case '\b': sb.Append("\\b"); break;
					case '\f': sb.Append("\\f"); break;
					case '\n': sb.Append("\\n"); break;
					case '\r': sb.Append("\\r"); break;
					case '\t': sb.Append("\\t"); break;
					default:
						if (c < 0x20)
							sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
						else
							sb.Append(c);
						break;
				}
			}
			sb.Append('"');
			return sb.ToString();
		}

		private static string BuildScript(string payloadBase64) => ScriptTemplate.Replace("__PAYLOAD__", payloadBase64);

		private const string ScriptTemplate = """
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class LrsPrivilege
{
    [StructLayout(LayoutKind.Sequential)]
    struct LUID { public uint LowPart; public int HighPart; }
    [StructLayout(LayoutKind.Sequential)]
    struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public LUID Luid; public uint Attributes; }
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool OpenProcessToken(IntPtr h, uint a, out IntPtr t);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool LookupPrivilegeValue(string s, string n, out LUID l);
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool AdjustTokenPrivileges(IntPtr t, bool d, ref TOKEN_PRIVILEGES n, uint b, IntPtr p, IntPtr r);
    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr h);
    public static void Enable(string name)
    {
        IntPtr tok;
        if (!OpenProcessToken(GetCurrentProcess(), 0x20 | 0x8, out tok)) return;
        try
        {
            LUID luid;
            if (!LookupPrivilegeValue(null, name, out luid)) return;
            var tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = 0x2 };
            AdjustTokenPrivileges(tok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally { CloseHandle(tok); }
    }
}
'@

[LrsPrivilege]::Enable('SeTakeOwnershipPrivilege')
[LrsPrivilege]::Enable('SeRestorePrivilege')

$b64 = '__PAYLOAD__'
$jsonText = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($b64))
$payload = $jsonText | ConvertFrom-Json

try {
    $path = [string]$payload.path
    $isDir = [bool]$payload.isDirectory
    $sections = [System.Security.AccessControl.AccessControlSections]::Access
    if ($null -ne $payload.owner) { $sections = $sections -bor [System.Security.AccessControl.AccessControlSections]::Owner }

    if ($isDir) {
        $info = New-Object System.IO.DirectoryInfo -ArgumentList $path
    } else {
        $info = New-Object System.IO.FileInfo -ArgumentList $path
    }
    $sec = $info.GetAccessControl($sections)

    if ($null -ne $payload.rules) {
        foreach ($r in @($payload.rules)) {
            $identity = [string]$r.identity
            $existing = @($sec.GetAccessRules($true, $false, [System.Security.Principal.NTAccount]) | Where-Object { $_.IdentityReference.Value -eq $identity })
            foreach ($e in $existing) { [void]$sec.RemoveAccessRuleSpecific($e) }

            $inherit = [System.Security.AccessControl.InheritanceFlags]([int]$r.inheritance)
            $propagate = [System.Security.AccessControl.PropagationFlags]([int]$r.propagation)
            $allow = [long]$r.allow
            $deny = [long]$r.deny
            $acct = New-Object System.Security.Principal.NTAccount -ArgumentList $identity
            if ($allow -ne 0) {
                $rule = New-Object System.Security.AccessControl.FileSystemAccessRule -ArgumentList $acct, ([System.Security.AccessControl.FileSystemRights]$allow), $inherit, $propagate, ([System.Security.AccessControl.AccessControlType]::Allow)
                [void]$sec.AddAccessRule($rule)
            }
            if ($deny -ne 0) {
                $rule = New-Object System.Security.AccessControl.FileSystemAccessRule -ArgumentList $acct, ([System.Security.AccessControl.FileSystemRights]$deny), $inherit, $propagate, ([System.Security.AccessControl.AccessControlType]::Deny)
                [void]$sec.AddAccessRule($rule)
            }
        }
    }

    if ($null -ne $payload.owner) {
        $ownerAcct = New-Object System.Security.Principal.NTAccount -ArgumentList ([string]$payload.owner)
        $sec.SetOwner($ownerAcct)
    }

    $info.SetAccessControl($sec)
}
catch {
    if ($null -ne $payload.errorFile) {
        try { [System.IO.File]::WriteAllText([string]$payload.errorFile, $_.Exception.ToString()) } catch { }
    }
    throw
}
""";
	}
}
