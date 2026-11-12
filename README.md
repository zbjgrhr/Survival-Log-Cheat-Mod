# Survival Log Cheat Mod

English · [简体中文](README.zh-CN.md)

A Windows trainer for the single-player Steam game [Survival Log](https://store.steampowered.com/app/4164790/Survival_Log/). It pairs a WinForms control panel with a BepInEx IL2CPP plug-in. The panel displays the current connection and reports action results. The plug-in executes changes inside the running game.

## Features

| Area | Available controls |
| --- | --- |
| Character and resources | Adjust selected character values, add gold, faith points, or survival points, and toggle effects for stamina, satiety, morale, and carrying weight. |
| Time and exploration | Change movement speed or the rate of time, move the clock forward or backward by one hour, and change travel time or exposure behavior. Map actions can restore fallen locations and replenish configured shop stock. |
| Items and facilities | Extend shelf life, repair supported spoiled items, and adjust selected item uses, durability, growth, power, or fuel consumption. |
| Inventory and storage | Resize supported backpack and storage grids. Independent storage points have their own contents and dimensions, with a maximum of 50 × 50 cells per point. |

## Requirements

- Windows and a local Steam installation of Survival Log.
- BepInEx for Windows x64 Unity IL2CPP. Start the game with BepInEx once to generate the interop assemblies in `BepInEx/interop`.
- A .NET SDK for the plug-in and the Windows .NET Framework C# compiler for the control panel.

The source below builds plug-in **0.5.11**. Independent storage is enabled only when the game assembly passes the plug-in's build check. Back up the game save and `BepInEx/config/SurvivalLogTrainer/save-settings` together before testing expanded or independent storage.

## Build and run

1. Set `$gameDir` to your game folder. In `01-Current-Source 当前源码/面板与图标/SurvivalLogPanelNew.cs`, set `GameDirectory` to that same folder.
2. From the repository root, build both components in PowerShell.

```powershell
$gameDir = 'D:\SteamLibrary\steamapps\common\Survival Log'
$src = '01-Current-Source 当前源码'
$plugin = Join-Path $src '后端插件'
$panel = (Resolve-Path (Join-Path $src '面板与图标')).Path
dotnet build (Join-Path $plugin 'SurvivalLogTrainer.csproj') -c Release "-p:GameDir=$gameDir"
if ($LASTEXITCODE -ne 0) { throw 'Plug-in build failed.' }

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
Push-Location -LiteralPath $panel
try {
    & $compiler /nologo /target:winexe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /win32icon:SurvivalLogPanel.ico /out:SurvivalLogPanel.exe SurvivalLogPanelNew.cs
    if ($LASTEXITCODE -ne 0) { throw 'Panel build failed.' }
} finally { Pop-Location }
```

3. With the game closed, copy `后端插件/bin/Release/netstandard2.1/SurvivalLogTrainer.dll` from the source directory to `<game>/BepInEx/plugins/`. Keep one copy of the trainer plug-in in that folder.
4. Start the game and load a save. Open `面板与图标/SurvivalLogPanel.exe`. Wait for the panel to report a connection, then select a feature tab.
5. For independent storage, create a point in the panel, enable in-game interaction, and press **F6** near the point with in-game menus closed. The panel also offers a restore action for temporary effects.

## Source layout

- [Current source](01-Current-Source%20当前源码) contains the C# plug-in, WinForms panel, and interface materials. The plug-in applies its embedded backpack scrolling patch when the target HTML matches.
- [Trade and cooking UI patches](01-Current-Source%20当前源码/ui-patches) contain the optional scrolling and layout changes. They are separate from the plug-in. Verify each patch against a copy of the matching game HTML before applying it.
- The numbered archive directories retain earlier builds, design materials, and development records. Use the version in the current C# project when building the plug-in.

Developed by **Introvert Arcade**.
