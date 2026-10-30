Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$logPath = 'C:\Users\zbjgr\Documents\Codex\2026-10-21\1x-20x-axb-a-b-shun\work\trainer-ui-test.txt'
$lines = New-Object System.Collections.ArrayList
$root = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]18616616)
$items = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$gold = $items[87]
$value = $gold.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
[void]$lines.Add("gold-before=$($value.Current.Value)")
try { $value.SetValue('12345'); [void]$lines.Add('gold-set=ok') } catch { [void]$lines.Add("gold-set-error=$_") }
Start-Sleep -Milliseconds 300
[void]$lines.Add("gold-after=$($value.Current.Value)")
$food = $items[33]
$toggle = $food.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
[void]$lines.Add("food-before=$($toggle.Current.ToggleState)")
try { $toggle.Toggle(); [void]$lines.Add('food-toggle=ok') } catch { [void]$lines.Add("food-toggle-error=$_") }
Start-Sleep -Milliseconds 300
[void]$lines.Add("food-middle=$($toggle.Current.ToggleState)")
try { $toggle.Toggle(); [void]$lines.Add('food-restore=ok') } catch { [void]$lines.Add("food-restore-error=$_") }
Start-Sleep -Milliseconds 300
[void]$lines.Add("food-after=$($toggle.Current.ToggleState)")
$lines | Set-Content -LiteralPath $logPath -Encoding UTF8
