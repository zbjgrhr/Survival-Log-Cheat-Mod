Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$source = @'
using System;
using System.Runtime.InteropServices;
public class WindowRestore { [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n); [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h); }
'@
Add-Type $source
$process = Get-Process | Where-Object { $_.ProcessName -like '*v3' } | Select-Object -First 1
$handle = $process.MainWindowHandle
$wasIconic = [WindowRestore]::IsIconic($handle)
$showResult = [WindowRestore]::ShowWindow($handle, 9)
$foregroundResult = [WindowRestore]::SetForegroundWindow($handle)
$root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
$items = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$lines = New-Object System.Collections.ArrayList
[void]$lines.Add("pid=$($process.Id) handle=$handle window=$($root.Current.Name) elements=$($items.Count)")
[void]$lines.Add("iconicBefore=$wasIconic showResult=$showResult foregroundResult=$foregroundResult iconicAfter=$([WindowRestore]::IsIconic($handle))")
for ($i=0; $i -lt $items.Count; $i++) {
    $item = $items[$i]
    if ($item.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -or $item.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit) {
        [void]$lines.Add("$i $($item.Current.ControlType.ProgrammaticName) $($item.Current.Name)")
    }
}
$lines | Set-Content -LiteralPath 'C:\Users\zbjgr\Documents\Codex\2026-10-21\1x-20x-axb-a-b-shun\work\panel-inspection.txt' -Encoding UTF8
