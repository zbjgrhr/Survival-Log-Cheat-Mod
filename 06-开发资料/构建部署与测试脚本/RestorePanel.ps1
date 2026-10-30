$source = @'
using System;
using System.Runtime.InteropServices;
public class WindowRestore2 {
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
}
'@
Add-Type $source
$process = Get-Process | Where-Object { $_.ProcessName -like '*v3' } | Select-Object -First 1
$handle = $process.MainWindowHandle
$before = [WindowRestore2]::IsIconic($handle)
$show = [WindowRestore2]::ShowWindow($handle, 9)
$foreground = [WindowRestore2]::SetForegroundWindow($handle)
$after = [WindowRestore2]::IsIconic($handle)
"pid=$($process.Id) handle=$handle before=$before show=$show foreground=$foreground after=$after" | Set-Content -LiteralPath 'C:\Users\zbjgr\Documents\Codex\2026-10-21\1x-20x-axb-a-b-shun\work\panel-restore.txt'
