using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        bool first;
        using (var mutex = new Mutex(true, "Local\\SurvivalLogSafeTrainerPanel", out first))
        {
            if (!first)
            {
                MessageBox.Show("控制面板已经打开。", "末日生存日志作弊器", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrainerPanel());
        }
    }
}

internal sealed class TrainerPanel : Form
{
    private const string GameDirectory = @"C:\tool\steam\steamapps\common\Survival Log";
    private static readonly string BridgeDirectory = Path.Combine(GameDirectory, @"BepInEx\config\SurvivalLogTrainer");
    private static readonly string StatusPath = Path.Combine(BridgeDirectory, "status.txt");
    private static readonly string CommandPath = Path.Combine(BridgeDirectory, "command.txt");
    private readonly Color back = Color.FromArgb(19, 30, 34);
    private readonly Color surface = Color.FromArgb(30, 45, 50);
    private readonly Color ink = Color.FromArgb(232, 242, 239);
    private readonly Color muted = Color.FromArgb(164, 186, 183);
    private readonly Color green = Color.FromArgb(149, 219, 180);
    private readonly Label connection = new Label();
    private readonly Label stats = new Label();
    private readonly Label result = new Label();
    private readonly Label speedState = new Label();
    private readonly Label timeState = new Label();
    private readonly Label timeDiagnostic = new Label();
    private readonly Label skillState = new Label();
    private readonly Label bagState = new Label();
    private readonly Label burdenState = new Label();
    private readonly Label storageState = new Label();
    private readonly Label storageDiagnostic = new Label();
    private readonly Dictionary<string, Button> toggles = new Dictionary<string, Button>();
    private readonly TabControl tabs = new TabControl();
    private readonly Button goldButton;
    private readonly Button faithButton;
    private readonly Button survivalButton;
    private readonly Button speedButton;
    private readonly Button resetButton;
    private readonly ComboBox speedChoice = new ComboBox();
    private readonly ComboBox timeChoice = new ComboBox();
    private readonly ComboBox skillChoice = new ComboBox();
    private readonly NumericUpDown bagWidth = new NumericUpDown();
    private readonly NumericUpDown bagHeight = new NumericUpDown();
    private readonly NumericUpDown storageWidth = new NumericUpDown();
    private readonly NumericUpDown storageHeight = new NumericUpDown();
    private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    private readonly List<Button> actionButtons = new List<Button>();
    private string session = "";
    private int epoch = -1;
    private long sequence;
    private int pid;
    private bool connected;
    private DateTime lastPing = DateTime.MinValue;
    private DateTime lastAction = DateTime.MinValue;
    private DateTime lastValidStatus = DateTime.MinValue;
    private string pendingAction = "";

    public TrainerPanel()
    {
        Text = "末日生存日志作弊器";
        ClientSize = new Size(500, 453);
        MinimumSize = new Size(516, 492);
        MaximumSize = new Size(516, 492);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = back;
        ForeColor = ink;
        Font = new Font("Microsoft YaHei UI", 9F);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        var title = NewLabel("末日生存日志  /  控制面板", 20, 15, 460, 33, 15F, green);
        title.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold);
        connection.Text = "正在连接游戏…";
        SetupLabel(connection, 21, 53, 457, 23, 9F, muted);
        stats.Text = "金币 —    信念点 —    生存点 —";
        SetupLabel(stats, 21, 81, 457, 31, 10F, ink);
        stats.BackColor = surface;
        stats.TextAlign = ContentAlignment.MiddleCenter;

        tabs.SetBounds(21, 121, 458, 254);
        tabs.Font = new Font("Microsoft YaHei UI", 9F);
        Controls.Add(tabs);
        var resources = NewTab("资源");
        var player = NewTab("人物 / 背包");
        var storage = NewTab("储物工具");
        var items = NewTab("物品 / 设施");
        var world = NewTab("时间 / 解锁");

        goldButton = NewButton("金币 +100000", 12, 15, 204, 38, delegate { SendAction("ADD_GOLD", "100000", "金币 +100000"); }, resources);
        faithButton = NewButton("信念点 +6", 228, 15, 204, 38, delegate { SendAction("ADD_FAITH", "6", "信念点 +6"); }, resources);
        survivalButton = NewButton("生存点 +100", 12, 62, 204, 38, delegate { SendAction("ADD_SURVIVAL", "100", "生存点 +100"); }, resources);
        NewButton("技能全部升满", 228, 62, 204, 38, delegate { ConfirmPermanent("升满技能会写入当前存档，不能由还原按钮撤销。继续吗？", "MAX_SKILLS", "技能全部升满"); }, resources);
        NewButton("批量取回附近地面物品（直到背包满）", 12, 109, 420, 36, delegate { SendAction("RECOVER_NEARBY_DROPS", "6", "批量取回附近地面物品"); }, resources);
        NewLabel("按距离拾取玩家周围 6 米内物品，直到附近取完或背包无法再装。", 12, 153, 420, 42, 8F, muted, resources);

        speedState.Text = "移速：1x";
        SetupLabel(speedState, 12, 10, 154, 28, 9F, ink, player);
        SetupChoice(speedChoice, new object[] { "1x", "2x", "3x", "5x", "10x", "20x" }, 166, 10, 78, player);
        speedButton = NewButton("应用", 252, 9, 78, 30, delegate {
            string value = speedChoice.SelectedItem.ToString().TrimEnd('x');
            SendAction("MOVE", value, "移速 " + value + "x");
        }, player);
        AddToggle(player, "无限精力", "stamina", 12, 50);
        AddToggle(player, "无限饱腹", "satiety", 228, 50);
        AddToggle(player, "无限心态", "morale", 12, 90);
        AddToggle(player, "无限负重", "burden", 228, 90);
        bagState.Text = "背包格数：原始";
        SetupLabel(bagState, 12, 141, 128, 27, 9F, ink, player);
        SetupNumber(bagWidth, 145, 140, player);
        NewLabel("×", 198, 142, 20, 25, 9F, ink, player);
        SetupNumber(bagHeight, 219, 140, player);
        NewButton("应用尺寸", 278, 139, 154, 29, delegate { SendAction("BAG_SIZE", bagWidth.Value + "x" + bagHeight.Value, "背包尺寸"); }, player);
        burdenState.Text = "负重：—";
        SetupLabel(burdenState, 12, 176, 420, 24, 9F, ink, player);
        NewLabel("还原前请将超出原背包格子的物品移走。", 12, 200, 420, 20, 8F, muted, player);

        storageState.Text = "储物格数：原始";
        SetupLabel(storageState, 12, 14, 128, 27, 9F, ink, storage);
        SetupNumber(storageWidth, 145, 13, storage);
        NewLabel("×", 198, 15, 20, 25, 9F, ink, storage);
        SetupNumber(storageHeight, 219, 13, storage);
        NewButton("应用尺寸", 278, 12, 154, 29, delegate { SendAction("STORAGE_SIZE", storageWidth.Value + "x" + storageHeight.Value, "储物工具尺寸"); }, storage);
        AddToggle(storage, "储物工具无限负重", "storageburden", 12, 57);
        NewButton("还原储物设置", 228, 57, 204, 33, delegate { SendAction("RESET_STORAGE", "0", "还原储物设置"); }, storage);
        AddToggle(storage, "固定所有设置（当前存档）", "persistall", 12, 98, 420);
        storageDiagnostic.Text = "已识别容器配置：0";
        SetupLabel(storageDiagnostic, 12, 137, 420, 25, 9F, ink, storage);
        NewLabel("固定开关、倍率、格数和负重，跨阶段及重启恢复；加钱、时间增减等一次性操作不会重复执行。", 12, 164, 420, 34, 8F, muted, storage);
        NewLabel("取消固定不会立刻改数值；还原前请先移走超出原格数的物品。", 12, 199, 420, 25, 8F, muted, storage);

        AddToggle(items, "永久保鲜并修复可识别腐坏物", "shelf", 12, 15);
        AddToggle(items, "无限使用次数", "uses", 228, 15);
        AddToggle(items, "建筑 / 家具耐久", "durability", 12, 55);
        AddToggle(items, "植物快速成熟", "plants", 228, 55);
        AddToggle(items, "无限发电量", "generation", 12, 95);
        AddToggle(items, "无限电池电量", "battery", 228, 95);
        AddToggle(items, "发电机不耗燃料", "generatorfuel", 12, 135);
        AddToggle(items, "烹饪不耗燃料", "cookingfuel", 228, 135);
        NewButton("开始当前酿酒桶酿酒", 12, 175, 420, 32, delegate { SendAction("START_BREW", "1", "开始当前酿酒桶酿酒"); }, items);
        NewLabel("先在游戏中打开目标酿酒桶并放好原料；此按钮等同游戏原生“开始酿酒”。", 12, 205, 425, 18, 7.5F, muted, items);

        timeState.Text = "时间：1x";
        SetupLabel(timeState, 12, 11, 112, 28, 9F, ink, world);
        SetupChoice(timeChoice, new object[] { "0.25x", "0.5x", "1x", "2x", "5x", "10x", "20x" }, 130, 10, 95, world);
        NewButton("应用", 232, 9, 82, 30, delegate { SendAction("TIME", timeChoice.SelectedItem.ToString().TrimEnd('x'), "时间倍率"); }, world);
        timeDiagnostic.Text = "游戏时间：—";
        SetupLabel(timeDiagnostic, 325, 10, 107, 28, 8F, muted, world);
        skillState.Text = "熟练度：1x";
        SetupLabel(skillState, 12, 54, 112, 28, 9F, ink, world);
        SetupChoice(skillChoice, new object[] { "1x", "2x", "3x", "5x", "10x", "20x" }, 130, 53, 95, world);
        NewButton("应用", 232, 52, 82, 30, delegate { SendAction("SKILL_MULT", skillChoice.SelectedItem.ToString().TrimEnd('x'), "技能熟练度倍率"); }, world);
        NewButton("时间 +1 小时", 12, 94, 132, 33, delegate { SendAction("ADD_TIME", "3600", "时间 +1 小时"); }, world);
        NewButton("时间 -1 小时", 150, 94, 132, 33, delegate { SendAction("ADD_TIME", "-3600", "时间 -1 小时"); }, world);
        AddToggle(world, "0 通行时间", "zerotravel", 288, 94, 144);
        NewButton("解锁全部图鉴", 12, 136, 204, 38, delegate { ConfirmPermanent("解锁图鉴可能写入跨存档记录，不能用还原按钮撤销。继续吗？", "UNLOCK_CODEX", "解锁全部图鉴"); }, world);
        NewButton("解锁全部成就", 228, 136, 204, 38, delegate { ConfirmPermanent("Steam 成就可能永久记入账号，无法还原。确定解锁吗？", "UNLOCK_ACHIEVEMENTS", "解锁全部成就"); }, world);
        AddToggle(world, "探索不增加暴露值", "noexposure", 12, 183, 420);

        resetButton = NewButton("还原所有临时效果", 21, 386, 158, 36, delegate { SendAction("RESET", "0", "还原所有临时效果"); });

        result.Text = "状态：等待游戏和插件就绪";
        SetupLabel(result, 192, 385, 287, 39, 8F, green);
        var note = NewLabel("临时效果断线后还原；固定设置按存档保留，取消固定后可还原。", 21, 427, 458, 20, 8F, muted);
        note.TextAlign = ContentAlignment.MiddleLeft;

        timer.Interval = 600;
        timer.Tick += delegate { Poll(); };
        timer.Start();
        FormClosing += delegate { if (connected) TrySend("RESET", "0"); };
        SetActionsEnabled(false);
        Poll();
    }

    private TabPage NewTab(string title)
    {
        var tab = new TabPage(title);
        tab.BackColor = back;
        tab.ForeColor = ink;
        tabs.TabPages.Add(tab);
        return tab;
    }

    private void SetupChoice(ComboBox choice, object[] options, int x, int y, int width, Control parent)
    {
        choice.DropDownStyle = ComboBoxStyle.DropDownList;
        choice.Items.AddRange(options);
        choice.SelectedIndex = 0;
        choice.SetBounds(x, y, width, 29);
        choice.BackColor = surface;
        choice.ForeColor = ink;
        parent.Controls.Add(choice);
    }

    private void SetupNumber(NumericUpDown number, int x, int y, Control parent)
    {
        number.Minimum = 1;
        number.Maximum = 50;
        number.Value = 10;
        number.SetBounds(x, y, 50, 28);
        number.BackColor = surface;
        number.ForeColor = ink;
        parent.Controls.Add(number);
    }

    private void AddToggle(Control parent, string caption, string key, int x, int y, int width = 204)
    {
        var button = NewButton(caption + "  [关]", x, y, width, 33, delegate {
            bool turnOn = buttonState(key) != "1";
            SendAction("TOGGLE", (turnOn ? "1:" : "0:") + key, caption + (turnOn ? "开启" : "关闭"));
        }, parent);
        toggles[key] = button;
        button.Tag = "0";
    }

    private string buttonState(string key) { return toggles.ContainsKey(key) ? Convert.ToString(toggles[key].Tag) : "0"; }

    private void ConfirmPermanent(string message, string verb, string description)
    {
        if (MessageBox.Show(message, "不可逆操作确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            SendAction(verb, "1", description);
    }

    private Label NewLabel(string text, int x, int y, int width, int height, float size, Color color, Control parent = null)
    {
        var label = new Label();
        label.Text = text;
        SetupLabel(label, x, y, width, height, size, color, parent);
        return label;
    }

    private void SetupLabel(Label label, int x, int y, int width, int height, float size, Color color, Control parent = null)
    {
        label.SetBounds(x, y, width, height);
        label.ForeColor = color;
        label.Font = new Font("Microsoft YaHei UI", size);
        label.AutoEllipsis = true;
        (parent ?? this).Controls.Add(label);
    }

    private Button NewButton(string text, int x, int y, int width, int height, EventHandler click, Control parent = null)
    {
        var button = new Button();
        button.Text = text;
        button.SetBounds(x, y, width, height);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Color.FromArgb(72, 105, 106);
        button.BackColor = surface;
        button.ForeColor = ink;
        button.Cursor = Cursors.Hand;
        button.Click += click;
        (parent ?? this).Controls.Add(button);
        actionButtons.Add(button);
        return button;
    }

    private void SetActionsEnabled(bool enabled)
    {
        foreach (var button in actionButtons) button.Enabled = enabled;
        speedChoice.Enabled = enabled;
        timeChoice.Enabled = enabled;
        skillChoice.Enabled = enabled;
        bagWidth.Enabled = enabled;
        bagHeight.Enabled = enabled;
        storageWidth.Enabled = enabled;
        storageHeight.Enabled = enabled;
    }

    private static Dictionary<string, string> ReadStatus()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using (var stream = new FileStream(StatusPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                int index = line.IndexOf('=');
                if (index > 0) values[line.Substring(0, index)] = line.Substring(index + 1);
            }
        }
        return values;
    }

    private static string Get(Dictionary<string, string> values, string name, string fallback = "")
    {
        string value;
        return values.TryGetValue(name, out value) ? value : fallback;
    }

    private void Poll()
    {
        try
        {
            if (!File.Exists(StatusPath))
            {
                if (DateTime.UtcNow - lastValidStatus < TimeSpan.FromSeconds(3)) return;
                Disconnect("未找到游戏插件；请先启动游戏");
                return;
            }
            var values = ReadStatus();
            int parsedPid, parsedEpoch;
            long parsedSequence, ticks;
            if (!int.TryParse(Get(values, "pid"), out parsedPid) ||
                !int.TryParse(Get(values, "epoch"), out parsedEpoch) ||
                !long.TryParse(Get(values, "sequence"), out parsedSequence) ||
                !long.TryParse(Get(values, "utc"), out ticks) ||
                DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc) > TimeSpan.FromSeconds(3))
            { Disconnect("游戏未运行或插件状态过期"); return; }
            try { using (var process = Process.GetProcessById(parsedPid)) { if (process.HasExited || process.ProcessName != "SurvivalLog") { Disconnect("游戏未运行"); return; } } }
            catch { Disconnect("游戏未运行"); return; }
            string newSession = Get(values, "session");
            if (newSession.Length != 32) { Disconnect("插件状态无效"); return; }
            lastValidStatus = DateTime.UtcNow;
            if (newSession != session || parsedEpoch != epoch || parsedPid != pid)
            {
                session = newSession;
                epoch = parsedEpoch;
                pid = parsedPid;
                sequence = parsedSequence;
                pendingAction = "";
                lastPing = DateTime.MinValue;
            }
            sequence = Math.Max(sequence, parsedSequence);
            connected = Get(values, "ready") == "1";
            connection.Text = connected ? "● 已连接当前存档  ·  固定设置仅作用于本存档" : "● 游戏已连接；请进入存档";
            connection.ForeColor = connected ? green : muted;
            string patchErrors = Get(values, "patch_errors");
            if (patchErrors.Length != 0)
            {
                connection.Text = "⚠ 部分功能加载失败：" + patchErrors;
                connection.ForeColor = Color.FromArgb(238, 185, 113);
            }
            SetActionsEnabled(connected);
            stats.Text = "金币 " + FormatNumber(Get(values, "gold")) + "    信念点 " + FormatNumber(Get(values, "faith")) + "    生存点 " + FormatNumber(Get(values, "survival"));
            string move = Get(values, "move", "1");
            if (move == "1") speedState.Text = "移速倍率：1x（关闭）";
            else if (Get(values, "move_hook_calls", "0") == "0") speedState.Text = "移速倍率：" + move + "x（待移动验证）";
            else speedState.Text = "移速倍率：" + move + "x（生效中）";
            timeState.Text = "时间：" + Get(values, "time", "1") + "x";
            timeDiagnostic.Text = "游戏秒数 " + Get(values, "game_seconds", "—");
            skillState.Text = "熟练度：" + Get(values, "skill_mult", "1") + "x";
            string bag = Get(values, "bag_size", "0x0");
            bagState.Text = bag == "0x0" ? "背包：原始" : "背包：" + bag.Replace('x', '×');
            burdenState.Text = "负重：" + FormatNumber(Get(values, "bag_weight")) + " / " + FormatNumber(Get(values, "bag_burden")) +
                "  ·  判定次数 " + Get(values, "burden_hook_calls", "0");
            string storageSize = Get(values, "storage_size", "0x0");
            storageState.Text = storageSize == "0x0" ? "储物格数：原始" : "储物格数：" + storageSize.Replace('x', '×');
            storageDiagnostic.Text = "已识别容器配置：" + Get(values, "storage_configs", "0") + "  ·  应用次数：" + Get(values, "storage_hits", "0");
            if (Get(values, "persiststorage", "0") == "1" && Get(values, "persistall", "0") != "1")
                storageDiagnostic.Text = "旧版背包 / 储物固定仍保留；可开启上方全部固定。";
            foreach (var entry in toggles)
            {
                string state = Get(values, entry.Key, "0");
                entry.Value.Tag = state;
                string caption = entry.Value.Text.Split(new[] { "  [" }, StringSplitOptions.None)[0];
                entry.Value.Text = caption + (state == "1" ? "  [开]" : "  [关]");
                entry.Value.BackColor = state == "1" ? Color.FromArgb(44, 86, 68) : surface;
            }
            string pluginResult = Get(values, "result", "就绪");
            if (parsedSequence >= sequence && pendingAction.Length != 0)
            {
                result.Text = "状态：" + pluginResult;
                pendingAction = "";
            }
            else if (pendingAction.Length == 0 && DateTime.UtcNow - lastAction > TimeSpan.FromSeconds(4))
                result.Text = "状态：" + pluginResult;

            if (connected && DateTime.UtcNow - lastPing > TimeSpan.FromSeconds(2) && pendingAction.Length == 0)
            {
                if (TrySend("PING", "0")) lastPing = DateTime.UtcNow;
            }
        }
        catch (IOException) { /* Status is being refreshed; retry next tick. */ }
        catch (UnauthorizedAccessException) { Disconnect("无法读取插件状态；请检查游戏目录权限"); }
        catch (Exception error) { Disconnect("连接错误：" + error.GetType().Name); }
    }

    private static string FormatNumber(string value)
    {
        int number;
        return int.TryParse(value, out number) && number >= 0 ? number.ToString("N0", CultureInfo.CurrentCulture) : "—";
    }

    private void Disconnect(string message)
    {
        connected = false;
        connection.Text = "○ " + message;
        connection.ForeColor = muted;
        stats.Text = "金币 —    信念点 —    生存点 —";
        speedState.Text = "移速倍率：—";
        timeState.Text = "时间：—";
        timeDiagnostic.Text = "游戏时间：—";
        skillState.Text = "熟练度：—";
        bagState.Text = "背包：—";
        burdenState.Text = "负重：—";
        storageState.Text = "储物格数：—";
        storageDiagnostic.Text = "已识别容器配置：—";
        result.Text = "状态：等待连接";
        SetActionsEnabled(false);
    }

    private void SendAction(string verb, string value, string description)
    {
        if (!connected) return;
        if (!TrySend(verb, value))
        {
            result.Text = "状态：命令发送失败；请检查游戏目录权限";
            return;
        }
        pendingAction = description;
        lastAction = DateTime.UtcNow;
        result.Text = "状态：正在执行 " + description + "…";
    }

    private bool TrySend(string verb, string value)
    {
        long nextSequence = sequence + 1;
        string line = session + "|" + epoch.ToString(CultureInfo.InvariantCulture) + "|" +
            nextSequence.ToString(CultureInfo.InvariantCulture) + "|" + verb + "|" + value;
        string temporaryPath = CommandPath + ".new";
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                File.WriteAllText(temporaryPath, line, new UTF8Encoding(false));
                if (File.Exists(CommandPath)) File.Replace(temporaryPath, CommandPath, null);
                else File.Move(temporaryPath, CommandPath);
                sequence = nextSequence;
                return true;
            }
            catch (IOException) { Thread.Sleep(15); }
            catch { return false; }
        }
        return false;
    }
}
