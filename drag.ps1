Add-Type -AssemblyName System.Windows.Forms
$p = Get-Process FastFluentFilesFolders -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if ($null -eq $p) { Write-Output 'NO_WINDOW'; exit 1 }
$sig = @'
using System;
using System.Runtime.InteropServices;
public struct RECT { public int Left, Top, Right, Bottom; }
public class Input32 {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
}
'@
Add-Type -TypeDefinition $sig
[Input32]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 400
$rect = New-Object RECT
[Input32]::GetWindowRect($p.MainWindowHandle, [ref]$rect) | Out-Null
$sx = $rect.Left + 120
$sy = $rect.Top + 300
$ex = $rect.Left + 120
$ey = $rect.Top + 520
[Input32]::SetCursorPos($sx, $sy) | Out-Null
Start-Sleep -Milliseconds 200
[Input32]::mouse_event(0x02, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 120
for ($i = 1; $i -le 10; $i++) {
  $y = $sy + [int](($ey - $sy) * $i / 10)
  [Input32]::SetCursorPos($sx, $y) | Out-Null
  Start-Sleep -Milliseconds 50
}
Start-Sleep -Milliseconds 200
[Input32]::mouse_event(0x04, 0, 0, 0, [UIntPtr]::Zero)
Write-Output ('dragged from (' + $sx + ',' + $sy + ') to (' + $ex + ',' + $ey + ') rect=(' + $rect.Left + ',' + $rect.Top + ')')
