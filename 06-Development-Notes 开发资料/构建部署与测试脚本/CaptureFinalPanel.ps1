Add-Type -AssemblyName System.Drawing
$source = @'
using System;
using System.Runtime.InteropServices;
public class ScreenFinal {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
}
'@
Add-Type $source
[void][ScreenFinal]::SetProcessDPIAware()
$process = Get-Process -Id 8876
$rect = New-Object ScreenFinal+RECT
[void][ScreenFinal]::GetWindowRect($process.MainWindowHandle, [ref]$rect)
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
$bitmap = New-Object System.Drawing.Bitmap $width,$height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$dc = $graphics.GetHdc()
$ok = [ScreenFinal]::PrintWindow($process.MainWindowHandle,$dc,2)
$graphics.ReleaseHdc($dc)
$bitmap.Save('C:\Users\zbjgr\Documents\Codex\2026-10-21\1x-20x-axb-a-b-shun\work\panel-final.png')
$graphics.Dispose()
$bitmap.Dispose()
"bounds=$($rect.Left),$($rect.Top),$width,$height print=$ok" | Set-Content -LiteralPath 'C:\Users\zbjgr\Documents\Codex\2026-10-21\1x-20x-axb-a-b-shun\work\panel-final-capture.txt'
