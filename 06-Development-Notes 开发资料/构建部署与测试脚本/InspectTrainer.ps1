$ErrorActionPreference = 'Continue'
$logPath = 'C:\Users\zbjgr\Documents\Codex\2026-10-21\1x-20x-axb-a-b-shun\work\trainer-ui-inspection.txt'
$script:lines = New-Object System.Collections.ArrayList
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$source = @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public class WinCheck {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int command);
}
'@
Add-Type $source
$process = Get-Process | Where-Object { $_.ProcessName -eq 'Survival Log v1.0 Plus 28 Trainer' } | Select-Object -First 1
[void]$script:lines.Add("Process: $($process.Id)")
[WinCheck]::EnumWindows({ param($handle, $unused)
    $id = [uint32]0
    [void][WinCheck]::GetWindowThreadProcessId($handle, [ref]$id)
    if ($id -eq $process.Id) {
        $title = New-Object System.Text.StringBuilder 512
        [void][WinCheck]::GetWindowText($handle, $title, 512)
        [void]$script:lines.Add("Window $handle visible=$([WinCheck]::IsWindowVisible($handle)) title=$title")
        try {
            $element = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
            $items = $element.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
            [void]$script:lines.Add("  descendants=$($items.Count)")
            for ($i=0; $i -lt [Math]::Min(300,$items.Count); $i++) {
                $item = $items[$i]
                $extra = "enabled=$($item.Current.IsEnabled) visible=$($item.Current.IsOffscreen)"
                $pattern = $null
                if ($item.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern,[ref]$pattern)) {
                    $extra += " value=$($pattern.Current.Value)"
                }
                $pattern = $null
                if ($item.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern,[ref]$pattern)) {
                    $extra += " toggle=$($pattern.Current.ToggleState)"
                }
                [void]$script:lines.Add("  $i : $($item.Current.ControlType.ProgrammaticName) | $($item.Current.Name) | $($item.Current.AutomationId) | $extra")
            }
        } catch { [void]$script:lines.Add("  UIA error: $_") }
    }
    return $true
}, [IntPtr]::Zero) | Out-Null
$script:lines | Set-Content -LiteralPath $logPath -Encoding UTF8
