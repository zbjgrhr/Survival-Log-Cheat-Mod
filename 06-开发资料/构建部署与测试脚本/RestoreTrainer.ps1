$source = @'
using System;
using System.Runtime.InteropServices;
public class WindowRestore3 {
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
'@
Add-Type $source
$result1 = [WindowRestore3]::ShowWindow([IntPtr]18616616, 9)
$result2 = [WindowRestore3]::SetForegroundWindow([IntPtr]18616616)
"show=$result1 foreground=$result2" | Set-Content -LiteralPath 'C:\Users\zbjgr\Documents\Codex\2026-10-21\1x-20x-axb-a-b-shun\work\trainer-restore.txt'
