$ErrorActionPreference = 'Stop'
$log = 'C:\Users\zbjgr\Documents\Codex\2026-10-21\1x-20x-axb-a-b-shun\work\panel-v5-launch.txt'
try {
    $target = 'C:\Users\zbjgr\Desktop\项目源码包\末日生存日志作弊器\末日生存日志控制面板.exe'
    $source = 'C:\Users\zbjgr\Documents\Codex\2026-10-21\1x-20x-axb-a-b-shun\work\末日生存日志控制面板-v5.3.exe'
    if (-not (Test-Path -LiteralPath $target)) { throw 'Panel executable is missing.' }
    if (-not (Test-Path -LiteralPath $source)) { throw 'New panel executable is missing.' }
    foreach ($name in @('末日生存日志控制面板-v4', '末日生存日志控制面板-v5', '末日生存日志控制面板')) {
        Get-Process -Name $name -ErrorAction SilentlyContinue | Stop-Process -Force
    }
    Copy-Item -LiteralPath $source -Destination $target -Force
    $panel = Start-Process -FilePath $target -WorkingDirectory (Split-Path -Parent $target) -PassThru
    Start-Sleep -Milliseconds 800
    $panel.Refresh()
    "pid=$($panel.Id) window=$($panel.MainWindowHandle) responding=$($panel.Responding)" | Set-Content -LiteralPath $log -Encoding UTF8
} catch {
    $_ | Out-String | Set-Content -LiteralPath $log -Encoding UTF8
    throw
}
