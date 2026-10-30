$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$gameRoot = 'C:\tool\steam\steamapps\common\Survival Log'
$panelPath = 'C:\Users\zbjgr\Desktop\项目源码包\末日生存日志作弊器\末日生存日志作弊器.exe'
$backupPath = Join-Path $PSScriptRoot 'backups\before-050-20261027-064852'
if (Get-Process SurvivalLog -ErrorAction SilentlyContinue) { throw '游戏正在运行，未安装。' }
$panelProcesses = @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq $panelPath })
foreach ($entry in $panelProcesses) {
    $panelProcess = Get-Process -Id $entry.ProcessId
    if (-not $panelProcess.CloseMainWindow()) { throw '面板无法正常关闭，未安装。' }
    if (-not $panelProcess.WaitForExit(5000)) { throw '面板仍在运行，未安装。' }
}
$webRoot = Join-Path $gameRoot 'SurvivalLog_Data\StreamingAssets\WebUI'
$files = @(
    @{ Source=(Join-Path $PSScriptRoot 'trainer_mod\bin\Release\netstandard2.1\SurvivalLogTrainer.dll'); Destination=(Join-Path $gameRoot 'BepInEx\plugins\SurvivalLogTrainer.dll'); Backup='SurvivalLogTrainer.dll' },
    @{ Source=(Join-Path $PSScriptRoot '末日生存日志作弊器-0.5.0.exe'); Destination=$panelPath; Backup='末日生存日志作弊器.exe' },
    @{ Source=(Join-Path $PSScriptRoot 'webui-050\webui-bag.js'); Destination=(Join-Path $webRoot 'webui-bag.js'); Backup='webui-bag.js' },
    @{ Source=(Join-Path $PSScriptRoot 'webui-050\ToolTable.html'); Destination=(Join-Path $webRoot 'UI\ToolTable\ToolTable.html'); Backup='ToolTable.html' },
    @{ Source=(Join-Path $PSScriptRoot 'webui-050\BackpackUI.html'); Destination=(Join-Path $webRoot 'UI\BackpackUI\BackpackUI.html'); Backup='BackpackUI.html' }
)
foreach ($entry in $files) {
    if (-not (Test-Path -LiteralPath $entry.Source)) { throw ('Missing source: '+$entry.Source) }
    $backupFile = Join-Path $backupPath $entry.Backup
    if (-not (Test-Path -LiteralPath $backupFile)) { Copy-Item -LiteralPath $entry.Destination -Destination $backupFile }
}
$installed = @()
try {
    foreach ($entry in $files) {
        Copy-Item -LiteralPath $entry.Source -Destination $entry.Destination -Force
        $installed += $entry
        if ((Get-FileHash -LiteralPath $entry.Source).Hash -ne (Get-FileHash -LiteralPath $entry.Destination).Hash) { throw 'Installed hash mismatch' }
    }
} catch {
    foreach ($entry in $installed) { Copy-Item -LiteralPath (Join-Path $backupPath $entry.Backup) -Destination $entry.Destination -Force }
    throw
}
$files | ForEach-Object { Get-FileHash -LiteralPath $_.Destination } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'installed-050.json') -Encoding UTF8
$saveRoot = 'C:\Users\zbjgr\AppData\LocalLow\LLS\SLGame\Saves'
$differences = @()
foreach ($save in Get-ChildItem -LiteralPath (Join-Path $backupPath 'Saves') -File) {
    $current = Join-Path $saveRoot $save.Name
    if (-not (Test-Path -LiteralPath $current) -or (Get-FileHash -LiteralPath $current).Hash -ne (Get-FileHash -LiteralPath $save.FullName).Hash) { $differences += $save.Name }
}
Write-Output ('Installed '+$files.Count+' files. Save differences since backup: '+$differences.Count)
if ($differences.Count) { $differences }
