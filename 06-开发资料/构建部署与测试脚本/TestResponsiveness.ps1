$source = @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public class WinResponse {
    public delegate bool EnumChildProc(IntPtr hwnd, IntPtr state);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumChildProc callback, IntPtr state);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int length);
    [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wparam, IntPtr lparam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int command);
}
'@
Add-Type $source
$panel = Get-Process -Id 49996
$handle = $panel.MainWindowHandle
[void][WinResponse]::ShowWindow($handle,9)
$lines = New-Object System.Collections.ArrayList
[void]$lines.Add("pid=$($panel.Id) handle=$handle")
[WinResponse]::EnumChildWindows($handle,{param($child,$state)
    $name = New-Object System.Text.StringBuilder 128
    [void][WinResponse]::GetWindowText($child,$name,128)
    if ($name.Length -gt 0) { [void]$script:lines.Add("child=$child text=$name") }
    return $true
},[IntPtr]::Zero) | Out-Null
$watch = New-Object System.Diagnostics.Stopwatch
$max = 0.0
$failed = 0
for ($i=0; $i -lt 100; $i++) {
    $result = [IntPtr]::Zero
    $watch.Restart()
    $sent = [WinResponse]::SendMessageTimeout($handle,0,[IntPtr]::Zero,[IntPtr]::Zero,2,1000,[ref]$result)
    $watch.Stop()
    if ($sent -eq [IntPtr]::Zero) { $failed++ }
    if ($watch.Elapsed.TotalMilliseconds -gt $max) { $max=$watch.Elapsed.TotalMilliseconds }
    Start-Sleep -Milliseconds 25
}
[void]$lines.Add("maxLatencyMs=$max failed=$failed")
$refreshButton = [IntPtr]10488014
$clickMax = 0.0
$clickFailed = 0
for ($i=0; $i -lt 30; $i++) {
    $result = [IntPtr]::Zero
    $watch.Restart()
    $sent = [WinResponse]::SendMessageTimeout($refreshButton,0xF5,[IntPtr]::Zero,[IntPtr]::Zero,2,1000,[ref]$result)
    $watch.Stop()
    if ($sent -eq [IntPtr]::Zero) { $clickFailed++ }
    if ($watch.Elapsed.TotalMilliseconds -gt $clickMax) { $clickMax=$watch.Elapsed.TotalMilliseconds }
    Start-Sleep -Milliseconds 20
}
[void]$lines.Add("refreshClickMaxMs=$clickMax failed=$clickFailed")
$toggleButton = [IntPtr]7608846
$toggleTimes = New-Object System.Collections.ArrayList
for ($i=0; $i -lt 2; $i++) {
    $result = [IntPtr]::Zero
    $watch.Restart()
    $sent = [WinResponse]::SendMessageTimeout($toggleButton,0xF5,[IntPtr]::Zero,[IntPtr]::Zero,2,1000,[ref]$result)
    $watch.Stop()
    [void]$toggleTimes.Add("$($watch.Elapsed.TotalMilliseconds):$($sent -ne [IntPtr]::Zero)")
    Start-Sleep -Seconds 2
}
[void]$lines.Add("toggleClickMs=$($toggleTimes -join ',')")
$lines | Set-Content -LiteralPath 'C:\Users\zbjgr\Documents\Codex\2026-10-21\1x-20x-axb-a-b-shun\work\responsiveness.txt' -Encoding UTF8
