Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]18616616)
$items = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$lines = New-Object System.Collections.ArrayList
foreach ($pair in @(@(102,105),@(136,141),@(191,195))) {
    $button = $items[$pair[0]]
    $edit = $items[$pair[1]]
    $toggle = $button.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    $value = $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    [void]$lines.Add("option=$($pair[0]) before=$($toggle.Current.ToggleState) edit=$($edit.Current.IsEnabled) value=$($value.Current.Value)")
    if ($toggle.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On) {
        $toggle.Toggle()
        Start-Sleep -Seconds 2
        [void]$lines.Add("option=$($pair[0]) off=$($toggle.Current.ToggleState) edit=$($edit.Current.IsEnabled)")
        $toggle.Toggle()
        Start-Sleep -Milliseconds 250
        [void]$lines.Add("option=$($pair[0]) restored=$($toggle.Current.ToggleState) value=$($value.Current.Value)")
    }
}
$lines | Set-Content -LiteralPath 'C:\Users\zbjgr\Documents\Codex\2026-10-21\1x-20x-axb-a-b-shun\work\numeric-availability.txt' -Encoding UTF8
