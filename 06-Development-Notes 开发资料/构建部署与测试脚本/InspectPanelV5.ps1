$ErrorActionPreference = 'Stop'
$log = 'C:\Users\zbjgr\Documents\Codex\2026-10-21\1x-20x-axb-a-b-shun\work\panel-v5-ui.txt'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$panel = Get-Process -Id 19128
$root = [System.Windows.Automation.AutomationElement]::FromHandle($panel.MainWindowHandle)
$all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$output = [System.Collections.Generic.List[string]]::new()
$output.Add("count=$($all.Count)")
foreach ($item in $all) {
    if ($item.Current.Name -match '还原运行时效果|未连接原修改器|仅显示旧修改器状态|设置数值|切换') {
        $output.Add("$($item.Current.Name) enabled=$($item.Current.IsEnabled)")
    }
}
$output | Set-Content -LiteralPath $log -Encoding UTF8
