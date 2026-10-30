$ErrorActionPreference = 'Stop'
$source = 'C:\Users\zbjgr\Documents\Codex\2026-10-21\1x-20x-axb-a-b-shun\work\末日生存日志控制面板-v4.exe'
$target = 'C:\Users\zbjgr\Desktop\项目源码包\末日生存日志作弊器\末日生存日志控制面板.exe'
$log = 'C:\Users\zbjgr\Documents\Codex\2026-10-21\1x-20x-axb-a-b-shun\work\deploy-fast-panel.txt'
if (!(Test-Path -LiteralPath $source) -or !(Test-Path -LiteralPath $target)) { throw 'Source or target missing.' }
foreach ($id in @(8876,49996)) {
    $process = Get-Process -Id $id -ErrorAction SilentlyContinue
    if ($process -and $process.ProcessName -like '*控制面板*') {
        Stop-Process -Id $id -Force
        Wait-Process -Id $id -ErrorAction SilentlyContinue
    }
}
Copy-Item -LiteralPath $source -Destination $target -Force
$launched = Start-Process -FilePath $target -WorkingDirectory (Split-Path -Parent $target) -WindowStyle Normal -PassThru
Start-Sleep -Milliseconds 800
$sourceCode = @'
using System;
using System.Runtime.InteropServices;
public class RestoreFast { [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n); [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); }
'@
Add-Type $sourceCode
$launched.Refresh()
[void][RestoreFast]::ShowWindow($launched.MainWindowHandle,9)
[void][RestoreFast]::SetForegroundWindow($launched.MainWindowHandle)
"pid=$($launched.Id) bytes=$((Get-Item -LiteralPath $target).Length)" | Set-Content -LiteralPath $log -Encoding UTF8
