using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
        {
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true, Verb = "runas" });
            }
            catch (Exception error)
            {
                MessageBox.Show("读取原修改器状态需要管理员权限。\n" + error.Message, "末日生存日志控制面板", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return;
        }
        Application.Run(new TrainerPanel());
    }
}

internal sealed class TrainerPanel : Form
{
    private const string TrainerPath = @"C:\tool\steam\steamapps\common\Survival Log\末日生存日志作弊器.exe";
    private static readonly string HistoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        @"..\LocalLow\LLS\SLGame\Saves\HistorySave.bytes");
    private static readonly Color Background = Color.FromArgb(18, 27, 31);
    private static readonly Color Surface = Color.FromArgb(29, 42, 47);
    private static readonly Color Ink = Color.FromArgb(230, 240, 231);
    private static readonly Color Green = Color.FromArgb(172, 217, 160);
    private static readonly Color Amber = Color.FromArgb(242, 194, 124);
    private static readonly Color Muted = Color.FromArgb(160, 177, 172);
    private readonly Label connection;
    private readonly Label notice;
    private readonly System.Windows.Forms.Timer timer;
    private readonly BlockingCollection<Action> workQueue = new BlockingCollection<Action>();
    private readonly List<RowView> rows = new List<RowView>();
    private AutomationElement trainerRoot;
    private AutomationElement[] toggles;
    private AutomationElement[] edits;
    private int trainerPid;
    private int refreshQueued;
    private int lastGamePid;
    private bool restoring;
    private string lastSaveName;

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr state);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int capacity);
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr state);

    private static readonly string[] Names = {
        "无限饱腹", "无限心态", "无限精力", "无限生命", "无限物品使用次数",
        "无限物品保质期", "无限建筑/家具耐久", "无限发电量", "无限电池电量", "最高负重上限",
        "增加金币", "设置生存点", "设置信念点", "免费刷新生存规则", "最高技能熟练度",
        "技能熟练度倍率", "锁定当天时间", "时间流逝速度", "植物瞬间成熟", "瞬间烹饪",
        "无限烹饪燃料", "制作不消耗材料", "解锁无尽模式", "解锁全部图鉴", "解锁全部成就",
        "地图移动零耗时", "移动速度倍率", "游戏速度倍率"
    };
    private static readonly HashSet<int> Numeric = new HashSet<int> { 10, 11, 12, 15, 17, 26, 27 };
    private static readonly HashSet<int> OneShot = new HashSet<int> { 10, 23, 24 };
    private static readonly HashSet<int> GlobalPersistent = new HashSet<int> { 22, 23, 24 };

    public TrainerPanel()
    {
        Text = "末日生存日志作弊器 · 控制面板";
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        ClientSize = new Size(630, 440);
        MinimumSize = new Size(540, 330);
        MaximumSize = new Size(900, 760);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Background;
        ForeColor = Ink;
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;

        var header = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = Surface };
        header.Controls.Add(new Label {
            Text = "SURVIVAL LOG  /  控制台", Location = new Point(15, 9), AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold), ForeColor = Green
        });
        connection = new Label { Text = "正在连接原修改器…", Location = new Point(17, 45), AutoSize = true, ForeColor = Amber };
        header.Controls.Add(connection);
        Controls.Add(header);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Surface };
        var launch = MakeButton("启动原修改器", 114);
        launch.Location = new Point(12, 7);
        launch.Click += delegate { workQueue.Add(delegate { LaunchTrainer(); PostSnapshot(ReadSnapshot()); }); };
        footer.Controls.Add(launch);
        var refresh = MakeButton("刷新状态", 86);
        refresh.Location = new Point(135, 7);
        refresh.Click += delegate { RefreshState(); };
        footer.Controls.Add(refresh);
        var restore = MakeButton("还原运行时效果", 126);
        restore.Location = new Point(230, 7);
        restore.Click += delegate { RestoreRuntime(); };
        footer.Controls.Add(restore);
        notice = new Label {
            Text = "仅显示旧修改器状态，不代表游戏内生效；游戏更新后可能失效。",
            Location = new Point(13, 42), AutoSize = true, ForeColor = Muted,
            Font = new Font("Microsoft YaHei UI", 8F)
        };
        footer.Controls.Add(notice);
        Controls.Add(footer);

        var tabs = new TabControl { Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei UI", 9F) };
        AddTab(tabs, "生存", 0, 10);
        AddTab(tabs, "资源成长", 10, 16);
        AddTab(tabs, "时间世界", 16, 28);
        Controls.Add(tabs);
        tabs.BringToFront();

        var worker = new Thread(delegate () {
            foreach (Action action in workQueue.GetConsumingEnumerable())
            {
                try { action(); }
                catch (Exception error) { PostError(error.Message, "后台操作失败"); }
            }
        });
        worker.IsBackground = true;
        worker.SetApartmentState(ApartmentState.MTA);
        worker.Start();

        timer = new System.Windows.Forms.Timer { Interval = 3000 };
        timer.Tick += delegate { RefreshState(); };
        timer.Start();
        Shown += delegate { RefreshState(); };
        FormClosed += delegate { timer.Stop(); workQueue.CompleteAdding(); };
    }

    private static Button MakeButton(string text, int width)
    {
        var button = new Button {
            Text = text, Width = width, Height = 28, FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(55, 78, 74), ForeColor = Ink,
            Font = new Font("Microsoft YaHei UI", 8.5F), UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(78, 106, 99);
        return button;
    }

    private void AddTab(TabControl tabs, string title, int from, int to)
    {
        var page = new TabPage(title) { BackColor = Background, Padding = new Padding(6) };
        var list = new FlowLayoutPanel {
            Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, BackColor = Background, Padding = new Padding(2)
        };
        page.Controls.Add(list);
        page.Resize += delegate {
            foreach (RowView row in rows.Where(r => r.Index >= from && r.Index < to))
                row.Panel.Width = Math.Max(480, page.ClientSize.Width - 30);
        };
        for (int index = from; index < to; index++)
        {
            int captured = index;
            var panel = new Panel {
                Width = 570, Height = 41, Margin = new Padding(0, 2, 0, 2), BackColor = Surface
            };
            var name = new Label {
                Text = Names[index], Location = new Point(10, 10), Size = new Size(180, 24),
                ForeColor = Ink, AutoEllipsis = true
            };
            var state = new Label {
                Text = "未知", Location = new Point(193, 10), Size = new Size(54, 24), ForeColor = Amber
            };
            panel.Controls.Add(name);
            panel.Controls.Add(state);
            TextBox input = null;
            Button apply = null;
            if (Numeric.Contains(index))
            {
                input = new TextBox {
                    Location = new Point(250, 8), Width = 82, Height = 25,
                    BackColor = Color.FromArgb(42, 57, 61), ForeColor = Ink, BorderStyle = BorderStyle.FixedSingle
                };
                input.Enabled = false;
                panel.Controls.Add(input);
                apply = MakeButton("设置数值", 76);
                apply.Enabled = false;
                apply.Location = new Point(339, 6);
                apply.Click += delegate { ApplyValue(captured); };
                panel.Controls.Add(apply);
            }
            var toggle = MakeButton(OneShot.Contains(index) ? "执行" : "切换", 70);
            toggle.Enabled = false;
            toggle.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            toggle.Location = new Point(panel.Width - 79, 6);
            toggle.Click += delegate { Toggle(captured); };
            panel.Resize += delegate { toggle.Left = panel.ClientSize.Width - 79; };
            panel.Controls.Add(toggle);
            rows.Add(new RowView(index, panel, state, input, apply, toggle));
            list.Controls.Add(panel);
        }
        tabs.TabPages.Add(page);
    }

    private bool Connect()
    {
        var processes = Process.GetProcessesByName("Survival Log v1.0 Plus 28 Trainer");
        if (processes.Length == 0) { trainerRoot = null; toggles = null; edits = null; trainerPid = 0; return false; }
        int pid = processes[0].Id;
        if (trainerRoot != null && trainerPid == pid)
        {
            try { string title = trainerRoot.Current.Name; if (title.Contains("生存日志")) return true; }
            catch { trainerRoot = null; toggles = null; edits = null; }
        }
        IntPtr hwnd = IntPtr.Zero;
        EnumWindows(delegate (IntPtr candidate, IntPtr unused) {
            uint owner;
            GetWindowThreadProcessId(candidate, out owner);
            if (owner == (uint)pid)
            {
                var title = new System.Text.StringBuilder(256);
                GetWindowText(candidate, title, title.Capacity);
                if (title.ToString().Contains("生存日志") && title.ToString().Contains("修改器"))
                    hwnd = candidate;
            }
            return true;
        }, IntPtr.Zero);
        if (hwnd == IntPtr.Zero) { trainerRoot = null; toggles = null; edits = null; return false; }
        trainerRoot = AutomationElement.FromHandle(hwnd);
        trainerPid = pid;
        toggles = null;
        edits = null;
        return true;
    }

    private bool ReadControls()
    {
        if (!Connect()) return false;
        if (toggles != null && edits != null && toggles.Length == Names.Length)
        {
            try { if (toggles[0].Current.AutomationId == "m_toggle_button") return true; }
            catch { toggles = null; edits = null; }
        }
        var all = trainerRoot.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        var buttons = new List<AutomationElement>();
        var values = new List<AutomationElement>();
        for (int i = 0; i < all.Count && buttons.Count < Names.Length; i++)
        {
            AutomationElement element = all[i];
            if (element.Current.AutomationId != "m_toggle_button" ||
                element.Current.ControlType != ControlType.Button) continue;
            AutomationElement edit = null;
            bool foundName = false;
            for (int j = i + 1; j < all.Count; j++)
            {
                AutomationElement child = all[j];
                if (child.Current.AutomationId == "m_toggle_button") break;
                if (child.Current.AutomationId == "m_label_option_name") foundName = true;
                if (child.Current.ControlType == ControlType.Edit && child.Current.AutomationId == "m_textbox") edit = child;
            }
            if (!foundName) continue;
            buttons.Add(element);
            values.Add(edit);
        }
        if (buttons.Count != Names.Length) return false;
        toggles = buttons.ToArray();
        edits = values.ToArray();
        return true;
    }

    private void RefreshState()
    {
        if (Interlocked.CompareExchange(ref refreshQueued, 1, 0) != 0) return;
        try {
            workQueue.Add(delegate {
                try { PostSnapshot(ReadSnapshot()); }
                finally { Interlocked.Exchange(ref refreshQueued, 0); }
            });
        }
        catch (InvalidOperationException) { Interlocked.Exchange(ref refreshQueued, 0); }
    }

    private Snapshot ReadSnapshot()
    {
        var snapshot = new Snapshot();
        try
        {
            snapshot.Connected = ReadControls();
            var gameProcesses = Process.GetProcessesByName("SurvivalLog");
            snapshot.Game = gameProcesses.Length > 0;
            snapshot.GamePid = snapshot.Game ? gameProcesses[0].Id : 0;
            string saveName = ReadActiveSaveName();
            bool processChanged = lastGamePid != 0 && lastGamePid != snapshot.GamePid;
            bool saveChanged = lastSaveName != null && saveName != null && lastSaveName != saveName;
            if ((processChanged || saveChanged) && snapshot.Connected)
            {
                snapshot.RestoreComplete = RestoreAllControls(saveChanged && snapshot.Game);
                snapshot.AutomaticRestore = true;
            }
            lastGamePid = snapshot.GamePid;
            if (saveName != null) lastSaveName = saveName;
            if (!snapshot.Connected) return snapshot;
            for (int i = 0; i < Names.Length; i++)
            {
                try
                {
                    var pattern = (TogglePattern)toggles[i].GetCurrentPattern(TogglePattern.Pattern);
                    snapshot.States[i] = pattern.Current.ToggleState == ToggleState.On;
                    if (edits[i] != null)
                    {
                        var value = (ValuePattern)edits[i].GetCurrentPattern(ValuePattern.Pattern);
                        snapshot.Values[i] = value.Current.Value;
                        snapshot.EditEnabled[i] = edits[i].Current.IsEnabled;
                    }
                }
                catch { snapshot.States[i] = null; }
            }
        }
        catch (Exception error) { trainerRoot = null; toggles = null; edits = null; snapshot.Error = error.Message; }
        return snapshot;
    }

    private void PostUi(Action action)
    {
        try { if (!IsDisposed && IsHandleCreated) BeginInvoke(action); }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    private void PostSnapshot(Snapshot snapshot)
    {
        PostUi(delegate {
            if (snapshot.Error != null)
            {
                connection.Text = "连接异常：" + snapshot.Error;
                connection.ForeColor = Amber;
            }
            else if (!snapshot.Connected)
            {
                connection.Text = "未连接原修改器 · 请启动游戏目录中的修改器";
                connection.ForeColor = Amber;
            }
            else
            {
                connection.Text = "游戏 " + (snapshot.Game ? "运行中" : "未运行") + "  ·  旧修改器已连接（游戏效果未验证）";
                connection.ForeColor = snapshot.Game ? Green : Amber;
            }
            if (snapshot.AutomaticRestore)
                notice.Text = snapshot.RestoreComplete
                    ? "检测到游戏进程或存档变化，已关闭旧修改器开关；游戏内请核对。"
                    : "检测到存档变化，但部分还原失败；请退出游戏和旧修改器后重开。";
            for (int i = 0; i < rows.Count; i++)
            {
                RowView row = rows[i];
                bool? on = snapshot.States[i];
                row.State.Text = GlobalPersistent.Contains(i) ? "全局禁用"
                    : on.HasValue ? (on.Value ? "修改器开" : "修改器关") : "未知";
                row.State.ForeColor = on.HasValue ? (on.Value ? Green : Muted) : Amber;
                row.Toggle.BackColor = on == true ? Color.FromArgb(65, 103, 71) : Color.FromArgb(55, 78, 74);
                row.Toggle.Enabled = snapshot.Connected && snapshot.Game && on.HasValue && !GlobalPersistent.Contains(i);
                if (row.Apply != null) row.Apply.Enabled = snapshot.Connected && snapshot.Game && snapshot.EditEnabled[i];
                if (row.Input != null) row.Input.Enabled = snapshot.Connected && snapshot.Game && snapshot.EditEnabled[i];
                if (row.Input != null && !row.Input.Focused && snapshot.Values[i] != null)
                    row.Input.Text = snapshot.Values[i];
            }
        });
    }

    private void PostError(string message, string title)
    {
        PostUi(delegate { MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning); });
    }

    private void Toggle(int index)
    {
        if (GlobalPersistent.Contains(index)) return;
        rows[index].State.Text = "处理中";
        workQueue.Add(delegate {
            try
            {
                if (!ReadControls()) throw new InvalidOperationException("未找到原修改器窗口；请先点击“启动原修改器”。");
                if (Process.GetProcessesByName("SurvivalLog").Length == 0)
                    throw new InvalidOperationException("游戏未运行，不能预先开启修改器开关。");
                ((TogglePattern)toggles[index].GetCurrentPattern(TogglePattern.Pattern)).Toggle();
                Thread.Sleep(120);
                PostUi(delegate { notice.Text = "已向旧修改器发送操作；请在游戏内核对是否生效。"; });
            }
            catch (Exception error) { PostError(error.Message, "切换失败"); }
            PostSnapshot(ReadSnapshot());
        });
    }

    private void ApplyValue(int index)
    {
        RowView row = rows[index];
        string supplied = row.Input.Text.Trim();
        double parsed;
        if (!double.TryParse(supplied, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ||
            double.IsNaN(parsed) || double.IsInfinity(parsed) || parsed <= 0 || parsed > 2000000000)
        {
            MessageBox.Show(this, "请输入大于 0 且不超过 20 亿的数字，使用小数点输入倍率。", "数值无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if ((index == 10 || index == 11 || index == 12) && parsed != Math.Floor(parsed))
        {
            MessageBox.Show(this, "金币和点数需要整数。", "数值无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (index == 26 && (parsed < 1 || parsed > 20))
        {
            MessageBox.Show(this, "移动速度倍率请设置在 1x–20x 之间。", "数值无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        row.State.Text = "处理中";
        workQueue.Add(delegate {
            bool turnedOff = false;
            try
            {
                if (!ReadControls()) throw new InvalidOperationException("未找到原修改器窗口；请先点击“启动原修改器”。");
                if (Process.GetProcessesByName("SurvivalLog").Length == 0)
                    throw new InvalidOperationException("游戏未运行，不能设置修改器数值。");
                if (edits[index] == null) throw new InvalidOperationException("原修改器没有提供此项数值框。");
                var toggle = (TogglePattern)toggles[index].GetCurrentPattern(TogglePattern.Pattern);
                if (toggle.Current.ToggleState == ToggleState.On && !edits[index].Current.IsEnabled)
                {
                    toggle.Toggle();
                    turnedOff = true;
                    Thread.Sleep(120);
                }
                if (!edits[index].Current.IsEnabled) throw new InvalidOperationException("原修改器当前锁定了这个数值框，不能直接设置；原来的开关状态已恢复。");
                var value = (ValuePattern)edits[index].GetCurrentPattern(ValuePattern.Pattern);
                value.SetValue(supplied);
                Thread.Sleep(100);
                string actual = value.Current.Value;
                if (turnedOff) { toggle.Toggle(); turnedOff = false; Thread.Sleep(120); }
                double actualNumber;
                if (!double.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out actualNumber) ||
                    Math.Abs(actualNumber - parsed) > 0.00001)
                    throw new InvalidOperationException("原修改器没有接受输入值；当前为 " + actual);
                PostUi(delegate { notice.Text = Names[index] + "：旧修改器设定框为 " + actual + "，不代表游戏内数值。"; });
            }
            catch (Exception error) { PostError(error.Message, "设置失败"); }
            finally
            {
                if (turnedOff)
                {
                    try { ((TogglePattern)toggles[index].GetCurrentPattern(TogglePattern.Pattern)).Toggle(); } catch { }
                }
                PostSnapshot(ReadSnapshot());
            }
        });
    }

    private void LaunchTrainer()
    {
        if (Process.GetProcessesByName("Survival Log v1.0 Plus 28 Trainer").Length > 0) return;
        if (!File.Exists(TrainerPath))
        {
            PostError("找不到：\n" + TrainerPath, "修改器未找到");
            return;
        }
        try { Process.Start(new ProcessStartInfo(TrainerPath) { WorkingDirectory = Path.GetDirectoryName(TrainerPath), UseShellExecute = true }); }
        catch (Exception error) { PostError(error.Message, "启动失败"); }
    }

    private void RestoreRuntime()
    {
        if (restoring) return;
        restoring = true;
        workQueue.Add(delegate {
            try
            {
                if (!ReadControls())
                {
                    PostUi(delegate { notice.Text = "原修改器未运行；没有可关闭的修改器开关。若游戏仍变快，请核查游戏内状态。"; });
                    return;
                }
                bool complete = RestoreAllControls(true);
                PostUi(delegate { notice.Text = complete
                    ? "已关闭旧修改器开关并尝试恢复游戏速度；游戏内请核对。"
                    : "部分还原失败；请退出游戏和旧修改器后重开，并核对游戏速度。"; });
            }
            catch (Exception error) { PostError(error.Message, "还原失败"); }
            finally
            {
                restoring = false;
                PostSnapshot(ReadSnapshot());
            }
        });
    }

    private bool RestoreAllControls(bool resetGameSpeed)
    {
        if (!ReadControls()) return false;
        bool complete = true;
        bool gameRunning = Process.GetProcessesByName("SurvivalLog").Length > 0;
        if (resetGameSpeed && gameRunning && edits[27] != null)
        {
            try
            {
                var speedToggle = (TogglePattern)toggles[27].GetCurrentPattern(TogglePattern.Pattern);
                if (speedToggle.Current.ToggleState == ToggleState.On) speedToggle.Toggle();
                if (edits[27].Current.IsEnabled)
                {
                    var value = (ValuePattern)edits[27].GetCurrentPattern(ValuePattern.Pattern);
                    value.SetValue("0");
                    if (value.Current.Value == "0")
                    {
                        speedToggle.Toggle();
                        Thread.Sleep(350);
                        if (speedToggle.Current.ToggleState == ToggleState.On) speedToggle.Toggle();
                    }
                    else complete = false;
                }
                else complete = false;
            }
            catch { complete = false; }
        }
        for (int i = 0; i < Names.Length; i++)
        {
            try
            {
                var toggle = (TogglePattern)toggles[i].GetCurrentPattern(TogglePattern.Pattern);
                if (toggle.Current.ToggleState == ToggleState.On) toggle.Toggle();
                if (toggle.Current.ToggleState == ToggleState.On) complete = false;
            }
            catch { complete = false; }
        }
        return complete;
    }

    private static string ReadActiveSaveName()
    {
        try
        {
            if (!File.Exists(HistoryPath)) return null;
            string data = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(HistoryPath));
            var matches = Regex.Matches(data, @"Save_\d{4}(?:_\d{2}){5}\.bytes");
            return matches.Count == 0 ? null : matches[matches.Count - 1].Value;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private sealed class Snapshot
    {
        public bool Connected;
        public bool Game;
        public int GamePid;
        public bool AutomaticRestore;
        public bool RestoreComplete;
        public string Error;
        public readonly bool?[] States = new bool?[Names.Length];
        public readonly string[] Values = new string[Names.Length];
        public readonly bool[] EditEnabled = new bool[Names.Length];
    }

    private sealed class RowView
    {
        public readonly int Index;
        public readonly Panel Panel;
        public readonly Label State;
        public readonly TextBox Input;
        public readonly Button Apply;
        public readonly Button Toggle;
        public RowView(int index, Panel panel, Label state, TextBox input, Button apply, Button toggle)
        { Index = index; Panel = panel; State = state; Input = input; Apply = apply; Toggle = toggle; }
    }
}
