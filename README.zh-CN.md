# 末日生存日志作弊器

简体中文 · [English](README.md)

末日生存日志作弊器是面向 Steam 单机游戏[《生存日志》](https://store.steampowered.com/app/4164790/Survival_Log/)的 Windows 修改工具，由 WinForms 控制面板和 BepInEx IL2CPP 插件组成。面板显示连接状态与执行结果，插件在运行中的游戏内执行修改。

## 主要功能

| 功能 | 调整范围 |
| --- | --- |
| 人物和资源 | 修改部分人物数值，增加金币、信念点或生存点，设置精力、饱腹、心态及负重相关效果。 |
| 时间和探索 | 调整移动速度和时间流速，按小时推进或回拨时间，调整通行时间和暴露值。地图工具支持恢复沦陷地点、补充配置中的商店库存。 |
| 物品和设施 | 延长保鲜时间，修复可识别的腐坏物，并可调整部分物品使用次数、设施耐久、植物成熟及电力和燃料消耗。 |
| 背包和储物 | 扩展背包及受支持的储物网格。独立储物点分别保存物品和尺寸，每处最多 50 × 50 格。 |

## 运行条件

- Windows 及本机 Steam 版《生存日志》。
- 适用于 Windows x64 Unity IL2CPP 的 BepInEx。安装后先启动一次游戏，生成 `BepInEx/interop` 中的互操作程序集。
- 构建后端需要 .NET SDK，构建面板需要 Windows .NET Framework 自带的 C# 编译器。

当前源码的后端版本为 **0.5.11**。独立储物功能会校验游戏程序集，校验通过后才可启用。测试扩容或独立储物前，请同时备份游戏存档和 `BepInEx/config/SurvivalLogTrainer/save-settings`。

## 构建与使用

1. 将下方 `$gameDir` 改为实际游戏目录。打开 `01-Current-Source 当前源码/面板与图标/SurvivalLogPanelNew.cs`，把 `GameDirectory` 设为同一目录。
2. 在仓库根目录通过 PowerShell 构建后端和面板。

```powershell
$gameDir = 'D:\SteamLibrary\steamapps\common\Survival Log'
$src = '01-Current-Source 当前源码'
$plugin = Join-Path $src '后端插件'
$panel = (Resolve-Path (Join-Path $src '面板与图标')).Path
dotnet build (Join-Path $plugin 'SurvivalLogTrainer.csproj') -c Release "-p:GameDir=$gameDir"
if ($LASTEXITCODE -ne 0) { throw '后端构建失败。' }

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
Push-Location -LiteralPath $panel
try {
    & $compiler /nologo /target:winexe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /win32icon:SurvivalLogPanel.ico /out:SurvivalLogPanel.exe SurvivalLogPanelNew.cs
    if ($LASTEXITCODE -ne 0) { throw '面板构建失败。' }
} finally { Pop-Location }
```

3. 退出游戏，将当前源码目录下 `后端插件/bin/Release/netstandard2.1/SurvivalLogTrainer.dll` 复制至游戏目录的 `BepInEx/plugins/`，该目录内只保留一份本插件。
4. 启动游戏并进入存档，打开 `面板与图标/SurvivalLogPanel.exe`。待面板显示已连接后，按需选择功能。
5. 使用独立储物时，先在面板内创建储物点并启用游戏内交互。关闭游戏菜单后，靠近储物点按 **F6** 打开。面板提供临时效果还原操作。

## 源码目录

- [当前源码](01-Current-Source%20当前源码)包含后端插件、Windows 面板和界面资料。游戏背包页面匹配时，插件会应用内置的滚动补丁。
- [可选界面补丁](01-Current-Source%20当前源码/ui-patches)涵盖交易、烹饪、工作台、共享背包滚动和开局商店。应用前请先用匹配版本的游戏文件副本核对补丁。使用「买100个」前，需应用商店补丁，并将 `trainer-buy100.js` 放在游戏的 `ShopUI.html` 同目录。面板启用后重新打开商店，按钮可批量加入购物车，结算仍由玩家确认。
- 其他编号目录保留历史构建、设计素材及开发资料。构建后端时以当前 C# 工程标注的版本为准。

开发者 **张铧心（发布名 Introvert Arcade）**。
