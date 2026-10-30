Add-Type -AssemblyName System.Drawing
$source = @'
using System;
using System.Runtime.InteropServices;
public class ScreenWin {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
'@
Add-Type $source
[void][ScreenWin]::SetProcessDPIAware()
$rect = New-Object ScreenWin+RECT
[void][ScreenWin]::GetWindowRect([IntPtr]18616616, [ref]$rect)
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
"bounds=$($rect.Left),$($rect.Top),$width,$height" | Set-Content -LiteralPath 'C:\Users\zbjgr\Documents\Codex\2026-10-21\1x-20x-axb-a-b-shun\work\trainer-bounds.txt'
$bitmap = New-Object System.Drawing.Bitmap $width,$height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($rect.Left,$rect.Top,0,0,[System.Drawing.Size]::new($width,$height))
$bitmap.Save('C:\Users\zbjgr\Documents\Codex\2026-10-21\1x-20x-axb-a-b-shun\work\trainer-window.png')
$graphics.Dispose()
$bitmap.Dispose()
