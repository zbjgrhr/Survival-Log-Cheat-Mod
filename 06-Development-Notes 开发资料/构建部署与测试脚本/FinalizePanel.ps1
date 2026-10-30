$source = @'
using System;
using System.Runtime.InteropServices;
public class FinalWindow {
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
'@
Add-Type $source
$old = Get-Process -Id 60740 -ErrorAction SilentlyContinue
if ($old) { Stop-Process -Id 60740 -Force }
$panel = Get-Process -Id 8876 -ErrorAction SilentlyContinue
if ($panel) {
    $handle = $panel.MainWindowHandle
    [void][FinalWindow]::ShowWindow($handle, 9)
    [void][FinalWindow]::SetForegroundWindow($handle)
    "panel=$($panel.Id) handle=$handle" | Set-Content -LiteralPath 'C:\Users\zbjgr\Documents\Codex\2026-10-21\1x-20x-axb-a-b-shun\work\final-panel-status.txt'
}
