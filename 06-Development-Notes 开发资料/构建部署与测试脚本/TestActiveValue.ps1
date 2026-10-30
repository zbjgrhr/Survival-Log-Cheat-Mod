Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]18616616)
$items = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$lines = New-Object System.Collections.ArrayList
$gold = $items[87].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
$gold.SetValue('1000000')
[void]$lines.Add("gold-restored=$($gold.Current.Value)")
$move = $items[191].GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
$speed = $items[195].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
[void]$lines.Add("move-before=$($move.Current.ToggleState) speed-before=$($speed.Current.Value)")
$move.Toggle()
Start-Sleep -Milliseconds 150
[void]$lines.Add("move-off=$($move.Current.ToggleState) edit-enabled=$($items[195].Current.IsEnabled)")
$speed.SetValue('2.5')
[void]$lines.Add("speed-set=$($speed.Current.Value)")
$move.Toggle()
Start-Sleep -Milliseconds 150
[void]$lines.Add("move-on=$($move.Current.ToggleState) speed-on=$($speed.Current.Value)")
$move.Toggle()
Start-Sleep -Milliseconds 150
$speed.SetValue('2.0')
$move.Toggle()
Start-Sleep -Milliseconds 150
[void]$lines.Add("move-restored=$($move.Current.ToggleState) speed-restored=$($speed.Current.Value)")
$lines | Set-Content -LiteralPath 'C:\Users\zbjgr\Documents\Codex\2026-10-21\1x-20x-axb-a-b-shun\work\active-value-test.txt' -Encoding UTF8
