using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using FirestormTwwMonitor.Core.Automation;
using FirestormTwwMonitor.Core.Gathering;
using FirestormTwwMonitor.Core.Input;
using FirestormTwwMonitor.Core.Memory;

namespace FirestormTwwMonitor.UI
{
    public class ProcessMonitorContext
    {
        public WowProcessItem ProcessInfo { get; set; } = new();
        public WowMemorySession Session { get; set; } = new();
        public ReconnectController ReconnectCtrl { get; set; } = null!;
        public GatheringTracker Tracker { get; set; } = null!;
        public bool IsMonitored { get; set; } = false;
    }

    public partial class MainForm : Form
    {
        // ── 动态多账号上下文列表 (支持 1、3、6、12+ 任意数量客户端) ─────
        private readonly List<ProcessMonitorContext> _contexts = new();
        private readonly object _lock = new();
        private ProcessMonitorContext? _selectedContext;
        private bool _isScanning = false;
        private bool _isSyncingUi = false;

        // ── 界面控件 ─────────────────────────────────────────────
        private Label _lblHeaderStats = null!;
        private ListView _list = null!;
        private Label _status = null!;
        private FlowLayoutPanel _toolbar = null!;

        // 选中账号控制卡片控件
        private Label _lblSelectedAccount = null!;
        private CheckBox _chkAutoReconnect = null!;
        private NumericUpDown _numInterval = null!;
        private Label _lblDetailStats = null!;

        // 按钮
        private Button _btnScan = null!;
        private Button _btnReconnectNow = null!;
        private Button _btnBottomReconnect = null!;
        private Button _btnStartAll = null!;
        private Button _btnStopAll = null!;
        private Button _btnCloseProc = null!;
        private Button _btnResetStats = null!;
        private Button _btnTestEnter = null!;

        // 定时器
        private readonly System.Windows.Forms.Timer _uiTimer = new();
        private readonly System.Windows.Forms.Timer _scanTimer = new();

        public MainForm()
        {
            InitializeComponent();

            _uiTimer.Interval = 1000;
            _uiTimer.Tick += (s, e) => UpdateUiTick();
            _uiTimer.Start();

            _scanTimer.Interval = 4000;
            _scanTimer.Tick += (s, e) =>
            {
                if (!_isScanning)
                {
                    Task.Run(() => ScanProcesses(false));
                }
            };
            _scanTimer.Start();

            Task.Run(() => ScanProcesses(true));
        }

        private static void SetDoubleBuffered(Control control)
        {
            typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(control, true);
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.Text = "Firestorm TWW 窗口监控器 · 多账号版";
            this.Size = new Size(880, 580);
            this.MinimumSize = new Size(820, 500);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = UiTheme.Background;
            this.Font = new Font("Microsoft YaHei UI", 9f);
            this.AutoScaleMode = AutoScaleMode.Dpi;

            // ── 主 TableLayoutPanel 布局 (彻底杜绝重叠与裁切) ──────────
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(14, 10, 14, 10),
                BackColor = UiTheme.Background
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56)); // 0: 顶部海军蓝 Header (高度适中，绝无文字被切)
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 1: 扁平现代工具栏 (自适应高度与自适应折行)
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); // 2: 状态提示横幅 (清晰明了)
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 3: 核心账号列表 (撑满剩余全部可用空间，随账号数量自动滚动)
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92)); // 4: 底部选中账号详情面板 (固定紧凑高度)

            // ── Row 0: 现代海军蓝 Header ───────────────────────────
            var header = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.Navy,
                Margin = new Padding(0, 0, 0, 6)
            };
            var title = new Label
            {
                Text = "FIRESTORM TWW MONITOR",
                Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(14, 6)
            };
            var subtitle = new Label
            {
                Text = "魔兽世界 11.2.5 (The War Within) · 多账号监控 · 断线自动重连 · 采集时速统计",
                Font = new Font("Microsoft YaHei UI", 8.5f),
                ForeColor = Color.FromArgb(148, 163, 184),
                AutoSize = true,
                Location = new Point(15, 30)
            };
            _lblHeaderStats = new Label
            {
                Text = "客户端: 0  |  监控中: 0  |  采集时速: 0.0/h",
                Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(147, 197, 253),
                BackColor = Color.FromArgb(30, 41, 59),
                Padding = new Padding(10, 5, 10, 5),
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(560, 12)
            };
            header.Controls.AddRange(new Control[] { title, subtitle, _lblHeaderStats });
            layout.Controls.Add(header, 0, 0);

            // ── Row 1: 扁平现代工具栏 (AutoSize + WrapContents，绝不截断) ──
            _toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = true,
                Margin = new Padding(0, 0, 0, 6)
            };

            _btnScan = new Button { Text = "🔍 刷新客户端", AutoSize = true, MinimumSize = new Size(95, 32) };
            _btnScan.Click += (s, e) => Task.Run(() => ScanProcesses(true));
            UiTheme.StyleButton(_btnScan);
            _btnScan.Margin = new Padding(0, 0, 8, 4);
            _toolbar.Controls.Add(_btnScan);

            _btnReconnectNow = new Button { Text = "🔄 立即重连", AutoSize = true, MinimumSize = new Size(95, 32) };
            _btnReconnectNow.Click += (s, e) => ReconnectNow();
            UiTheme.StyleButton(_btnReconnectNow, primary: true);
            _btnReconnectNow.Margin = new Padding(0, 0, 8, 4);
            _toolbar.Controls.Add(_btnReconnectNow);

            _btnStartAll = new Button { Text = "▶ 全部开始监控", AutoSize = true, MinimumSize = new Size(95, 32) };
            _btnStartAll.Click += (s, e) => StartAllMonitoring();
            UiTheme.StyleButton(_btnStartAll, primary: false);
            _btnStartAll.Margin = new Padding(0, 0, 8, 4);
            _toolbar.Controls.Add(_btnStartAll);

            _btnStopAll = new Button { Text = "⏹ 全部停止监控", AutoSize = true, MinimumSize = new Size(95, 32) };
            _btnStopAll.Click += (s, e) => StopAllMonitoring();
            UiTheme.StyleButton(_btnStopAll);
            _btnStopAll.Margin = new Padding(0, 0, 8, 4);
            _toolbar.Controls.Add(_btnStopAll);

            _btnCloseProc = new Button { Text = "❌ 关闭选中客户端", AutoSize = true, MinimumSize = new Size(115, 32) };
            _btnCloseProc.Click += (s, e) => CloseSelectedProcess();
            UiTheme.StyleButton(_btnCloseProc, danger: true);
            _btnCloseProc.Margin = new Padding(0, 0, 8, 4);
            _toolbar.Controls.Add(_btnCloseProc);

            _btnResetStats = new Button { Text = "🔄 重置采集统计", AutoSize = true, MinimumSize = new Size(95, 32) };
            _btnResetStats.Click += (s, e) => ResetGatheringStats();
            UiTheme.StyleButton(_btnResetStats);
            _btnResetStats.Margin = new Padding(0, 0, 8, 4);
            _toolbar.Controls.Add(_btnResetStats);

            _btnTestEnter = new Button { Text = "↵ 后台回车测试", AutoSize = true, MinimumSize = new Size(95, 32) };
            _btnTestEnter.Click += (s, e) => TestSendEnter();
            UiTheme.StyleButton(_btnTestEnter);
            _btnTestEnter.Margin = new Padding(0, 0, 0, 4);
            _toolbar.Controls.Add(_btnTestEnter);

            layout.Controls.Add(_toolbar, 0, 1);

            // ── Row 2: 状态提示横幅 (清晰浅蓝) ──────────────────────
            _status = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.FromArgb(224, 242, 254),
                ForeColor = Color.FromArgb(3, 105, 161),
                Padding = new Padding(12, 0, 12, 0),
                Margin = new Padding(0, 0, 0, 8),
                Text = "监控系统就绪。正在自动侦测魔兽世界客户端...",
                Font = new Font("Microsoft YaHei UI", 8.5f),
                AutoEllipsis = true
            };
            layout.Controls.Add(_status, 0, 2);

            // ── Row 3: 核心动态多账号表格 (高度自适应 100%，支持滚动) ───
            var tableCard = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(1),
                Margin = new Padding(0, 0, 0, 8)
            };

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                GridLines = false,
                CheckBoxes = true, // 勾选框：控制该客户端是否参与自动重连
                Scrollable = true, // 支持任意数量客户端 (3个、6个、12个等) 平滑滚动
                Font = new Font("Microsoft YaHei UI", 9f)
            };
            SetDoubleBuffered(_list);
            UiTheme.StyleList(_list);

            // 利用 ImageList 锁定每行高度为 28px
            var imgList = new ImageList { ImageSize = new Size(1, 28) };
            _list.SmallImageList = imgList;

            _list.Columns.Add("重连", 58);
            _list.Columns.Add("序号", 45);
            _list.Columns.Add("PID", 65);
            _list.Columns.Add("角色名称", 115);
            _list.Columns.Add("等级", 45);
            _list.Columns.Add("在线状态", 80);
            _list.Columns.Add("重连状态", 100);
            _list.Columns.Add("重连次", 55);
            _list.Columns.Add("矿草存量", 220);
            _list.Columns.Add("采集时速", 95);

            _list.ItemChecked += (s, e) =>
            {
                if (_isSyncingUi) return;

                if (e.Item.Tag is ProcessMonitorContext ctx)
                {
                    bool enable = e.Item.Checked;
                    ctx.ReconnectCtrl.AutoReconnectEnabled = enable;
                    ctx.IsMonitored = enable;
                    if (enable)
                    {
                        ctx.ReconnectCtrl.Start();
                        ctx.Tracker.Start();
                        SetStatus($"客户端 PID {ctx.ProcessInfo.Pid} 已开启自动重连监控");
                    }
                    else
                    {
                        ctx.ReconnectCtrl.Stop();
                        SetStatus($"客户端 PID {ctx.ProcessInfo.Pid} 已关闭自动重连监控");
                    }

                    if (_selectedContext == ctx && _chkAutoReconnect.Checked != enable)
                    {
                        _isSyncingUi = true;
                        try { _chkAutoReconnect.Checked = enable; }
                        finally { _isSyncingUi = false; }
                    }
                }
            };

            _list.SelectedIndexChanged += (s, e) =>
            {
                if (_list.SelectedItems.Count > 0)
                {
                    _selectedContext = _list.SelectedItems[0].Tag as ProcessMonitorContext;
                }
                else
                {
                    _selectedContext = null;
                }
                UpdateSelectedAccountCard();
            };

            var cms = new ContextMenuStrip();
            cms.Items.Add("🔄 立即重连进游戏 (4秒重试)", null, (s, e) => ReconnectNowSelected());
            cms.Items.Add(new ToolStripSeparator());
            cms.Items.Add("▶ 启动此客户端监控", null, (s, e) => ToggleSelectedMonitor(true));
            cms.Items.Add("⏹ 停止此客户端监控", null, (s, e) => ToggleSelectedMonitor(false));
            cms.Items.Add(new ToolStripSeparator());
            cms.Items.Add("🪟 激活置前窗口", null, (s, e) => BringSelectedToFront());
            cms.Items.Add("↵ 后台发送回车测试", null, (s, e) => TestSendEnter());
            cms.Items.Add(new ToolStripSeparator());
            cms.Items.Add("❌ 关闭游戏进程", null, (s, e) => CloseSelectedProcess());
            _list.ContextMenuStrip = cms;

            tableCard.Controls.Add(_list);
            layout.Controls.Add(tableCard, 0, 3);

            // ── Row 4: 底部选中账号详情与操作卡片 (固定紧凑高度 92px) ────
            var bottomCard = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(14, 8, 14, 8),
                Margin = Padding.Empty
            };

            var pnlRow1 = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 32,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight
            };

            _lblSelectedAccount = new Label
            {
                Text = "当前选中: [无]",
                Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
                ForeColor = UiTheme.Ink,
                AutoSize = true,
                Margin = new Padding(0, 5, 14, 0)
            };
            pnlRow1.Controls.Add(_lblSelectedAccount);

            _btnBottomReconnect = new Button
            {
                Text = "🔄 立即重连",
                AutoSize = true,
                MinimumSize = new Size(95, 28),
                Margin = new Padding(0, 2, 12, 0)
            };
            UiTheme.StyleButton(_btnBottomReconnect, primary: true);
            _btnBottomReconnect.Click += (s, e) => ReconnectNowSelected();
            pnlRow1.Controls.Add(_btnBottomReconnect);

            _chkAutoReconnect = new CheckBox
            {
                Text = "开启自动重连",
                Checked = true,
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 9f),
                Margin = new Padding(0, 4, 14, 0)
            };
            _chkAutoReconnect.CheckedChanged += (s, e) =>
            {
                if (_isSyncingUi) return;

                if (_selectedContext != null)
                {
                    bool enable = _chkAutoReconnect.Checked;
                    _selectedContext.ReconnectCtrl.AutoReconnectEnabled = enable;
                    _selectedContext.IsMonitored = enable;
                    if (enable)
                    {
                        _selectedContext.ReconnectCtrl.Start();
                        _selectedContext.Tracker.Start();
                        SetStatus($"客户端 PID {_selectedContext.ProcessInfo.Pid} 已开启自动重连监控");
                    }
                    else
                    {
                        _selectedContext.ReconnectCtrl.Stop();
                        SetStatus($"客户端 PID {_selectedContext.ProcessInfo.Pid} 已关闭自动重连监控");
                    }

                    _isSyncingUi = true;
                    try
                    {
                        for (int i = 0; i < _list.Items.Count; i++)
                        {
                            if (_list.Items[i].Tag == _selectedContext)
                            {
                                if (_list.Items[i].Checked != enable)
                                {
                                    _list.Items[i].Checked = enable;
                                }
                                break;
                            }
                        }
                    }
                    finally
                    {
                        _isSyncingUi = false;
                    }
                }
            };
            pnlRow1.Controls.Add(_chkAutoReconnect);

            var lblModeNotice = new Label
            {
                Text = "⚡ 模式: 纯后台消息 (不抢焦点)",
                AutoSize = true,
                ForeColor = Color.FromArgb(5, 150, 105),
                Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
                Margin = new Padding(0, 5, 14, 0)
            };
            pnlRow1.Controls.Add(lblModeNotice);

            var lblInterval = new Label { Text = "重试间隔(秒):", AutoSize = true, Margin = new Padding(0, 6, 4, 0) };
            _numInterval = new NumericUpDown { Minimum = 1, Maximum = 30, Value = 4, Width = 45, Margin = new Padding(0, 3, 0, 0) };
            _numInterval.ValueChanged += (s, e) =>
            {
                if (_selectedContext != null)
                {
                    _selectedContext.ReconnectCtrl.StepIntervalSeconds = (int)_numInterval.Value;
                }
            };
            pnlRow1.Controls.AddRange(new Control[] { lblInterval, _numInterval });

            bottomCard.Controls.Add(pnlRow1);

            // 第二行：详情汇总与采集存量显示（已移除手动校准存量按钮）
            var pnlRow2 = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 6, 0, 0)
            };

            _lblDetailStats = new Label
            {
                Text = "总矿: 0 | 总草: 0 | 背包空位: 0/0 | 本次采集: +0 | 计时: 00:00:00 | 时速: 0.0/h",
                Font = new Font("Microsoft YaHei UI", 9f),
                ForeColor = UiTheme.Muted,
                Dock = DockStyle.Left,
                AutoSize = true
            };
            pnlRow2.Controls.Add(_lblDetailStats);

            bottomCard.Controls.Add(pnlRow2);
            layout.Controls.Add(bottomCard, 0, 4);

            this.Controls.Add(layout);
            this.ResumeLayout(false);

            UpdateSelectedAccountCard();
        }

        // ── 进程扫描与动态账号列表同步 (支持 1~12+ 个客户端) ─────────
        private void ScanProcesses(bool manual)
        {
            _isScanning = true;
            try
            {
                var procs = WowProcessManager.FindWowProcesses();

                lock (_lock)
                {
                    // 1. 清理已关闭退出的客户端
                    for (int i = _contexts.Count - 1; i >= 0; i--)
                    {
                        var ctx = _contexts[i];
                        if (!procs.Any(p => p.Pid == ctx.ProcessInfo.Pid))
                        {
                            ctx.ReconnectCtrl.Stop();
                            ctx.Session.Dispose();
                            _contexts.RemoveAt(i);
                            SetStatus($"[退出] 客户端 PID {ctx.ProcessInfo.Pid} 已退出");
                        }
                    }

                    // 2. 挂接新启动的客户端
                    foreach (var p in procs)
                    {
                        var match = _contexts.FirstOrDefault(c => c.ProcessInfo.Pid == p.Pid);
                        if (match == null)
                        {
                            var ctx = new ProcessMonitorContext
                            {
                                ProcessInfo = p,
                                Session = new WowMemorySession()
                            };
                            ctx.ReconnectCtrl = new ReconnectController(ctx.Session);
                            ctx.Tracker = new GatheringTracker(ctx.Session);

                            int pid = p.Pid;
                            ctx.ReconnectCtrl.OnLogMessage += msg => SetStatus($"[{pid}] {msg}");
                            ctx.Session.Attach(pid, p.MainWindowHandle);
                            ctx.Session.CheckWorldState();
                            if (ctx.Session.IsInWorld)
                            {
                                ctx.Tracker.PollBagCounts();
                            }
                            ctx.IsMonitored = true;
                            ctx.ReconnectCtrl.Start();
                            ctx.Tracker.Start();
                            _contexts.Add(ctx);

                            SetStatus($"[发现] 新客户端 PID {pid} | 角色: {p.CharacterName}");
                        }
                        else
                        {
                            match.ProcessInfo.CharacterName = p.CharacterName;
                            match.ProcessInfo.WindowTitle = p.WindowTitle;
                        }
                    }
                }

                if (manual)
                {
                    SetStatus($"[扫描完毕] 当前检测到 {procs.Count} 个魔兽世界客户端");
                }

                if (this.InvokeRequired)
                {
                    this.BeginInvoke(new Action(SyncTableRows));
                }
                else
                {
                    SyncTableRows();
                }
            }
            catch (Exception ex)
            {
                SetStatus($"扫描异常: {ex.Message}");
            }
            finally
            {
                _isScanning = false;
            }
        }

        /// <summary>
        /// 原地动态同步表格数据：无闪烁、无清空、按客户端数量动态增减
        /// </summary>
        private void SyncTableRows()
        {
            _isSyncingUi = true;
            _list.BeginUpdate();
            try
            {
                List<ProcessMonitorContext> snapshot;
                lock (_lock)
                {
                    snapshot = _contexts.ToList();
                }

                // 更新 Header 指标
                int totalMonitored = snapshot.Count(c => c.IsMonitored);
                double totalSpeed = snapshot.Sum(c => (!c.Tracker.IsRunning || c.Tracker.TotalGained <= 0) ? 0.0 : c.Tracker.TotalPerHour);
                _lblHeaderStats.Text = $"客户端: {snapshot.Count}  |  监控中: {totalMonitored}  |  采集时速: {totalSpeed:F1}/h";

                // 调整行数以精确匹配当前客户端数量
                while (_list.Items.Count < snapshot.Count)
                {
                    int newIdx = _list.Items.Count;
                    var lvi = new ListViewItem("");
                    for (int s = 0; s < 9; s++) lvi.SubItems.Add("");
                    _list.Items.Add(lvi);
                }
                while (_list.Items.Count > snapshot.Count)
                {
                    _list.Items.RemoveAt(_list.Items.Count - 1);
                }

                // 原地快速刷新每行内容
                for (int i = 0; i < snapshot.Count; i++)
                {
                    var ctx = snapshot[i];
                    var lvi = _list.Items[i];
                    lvi.Tag = ctx;

                    // 同步勾选框（是否开启自动重连）
                    bool shouldBeChecked = ctx.ReconnectCtrl.AutoReconnectEnabled;
                    if (lvi.Checked != shouldBeChecked)
                    {
                        lvi.Checked = shouldBeChecked;
                    }

                    var session = ctx.Session;
                    string pidStr = ctx.ProcessInfo.Pid.ToString();
                    string charName = (session.IsAttached && session.IsInWorld) ? session.GetPlayerName() : ctx.ProcessInfo.CharacterName;
                    if (string.IsNullOrWhiteSpace(charName) || session.GameState == ClientGameState.CharSelect)
                    {
                        charName = "未选角(待进游戏)";
                    }

                    uint level = session.IsAttached ? session.GetPlayerLevel() : 0;
                    string levelStr = level > 0 ? level.ToString() : "-";
                    string worldState = session.GameState switch
                    {
                        ClientGameState.InWorld => "🟢 在线",
                        ClientGameState.CharSelect => "⏳ 待进游戏(未在线)",
                        _ => "🔴 离线"
                    };
                    string reconnectState = ctx.IsMonitored ? ctx.ReconnectCtrl.StatusDescription : "⏹ 未开启";
                    string recCountStr = ctx.ReconnectCtrl.ReconnectCount.ToString();

                    string bagSummary = FormatBagSummary(ctx.Tracker);
                    double rowSpeed = (!ctx.Tracker.IsRunning || ctx.Tracker.TotalGained <= 0) ? 0.0 : ctx.Tracker.TotalPerHour;
                    string speedStr = $"{rowSpeed:F1}/h";

                    string idxStr = $"#{i + 1}";
                    if (lvi.SubItems[1].Text != idxStr) lvi.SubItems[1].Text = idxStr;
                    if (lvi.SubItems[2].Text != pidStr) lvi.SubItems[2].Text = pidStr;
                    if (lvi.SubItems[3].Text != charName) lvi.SubItems[3].Text = charName;
                    if (lvi.SubItems[4].Text != levelStr) lvi.SubItems[4].Text = levelStr;
                    if (lvi.SubItems[5].Text != worldState) lvi.SubItems[5].Text = worldState;
                    if (lvi.SubItems[6].Text != reconnectState) lvi.SubItems[6].Text = reconnectState;
                    if (lvi.SubItems[7].Text != recCountStr) lvi.SubItems[7].Text = recCountStr;
                    if (lvi.SubItems[8].Text != bagSummary) lvi.SubItems[8].Text = bagSummary;
                    if (lvi.SubItems[9].Text != speedStr) lvi.SubItems[9].Text = speedStr;
                }

                // 保持当前选中状态
                if (_selectedContext != null && !snapshot.Contains(_selectedContext))
                {
                    _selectedContext = snapshot.FirstOrDefault();
                }

                if (_selectedContext != null)
                {
                    for (int i = 0; i < _list.Items.Count; i++)
                    {
                        if (_list.Items[i].Tag == _selectedContext)
                        {
                            if (!_list.Items[i].Selected) _list.Items[i].Selected = true;
                            break;
                        }
                    }
                }
                else if (_list.Items.Count > 0 && _list.SelectedItems.Count == 0)
                {
                    _list.Items[0].Selected = true;
                    _selectedContext = _list.Items[0].Tag as ProcessMonitorContext;
                }
            }
            finally
            {
                _list.EndUpdate();
                _isSyncingUi = false;
            }

            UpdateSelectedAccountCard();
        }

        private static string FormatBagSummary(GatheringTracker tracker)
        {
            if (tracker == null) return "-";
            int totalOre = tracker.TotalOreCount;
            int totalHerb = tracker.TotalHerbCount;
            int freeSlots = tracker.FreeSlots;

            return $"矿: {totalOre}  草: {totalHerb} | 空位: {freeSlots}";
        }

        private void UpdateSelectedAccountCard()
        {
            _isSyncingUi = true;
            try
            {
                var ctx = _selectedContext;
                if (ctx == null)
                {
                    _lblSelectedAccount.Text = "当前选中: [无活动客户端]";
                    _chkAutoReconnect.Enabled = false;
                    _btnBottomReconnect.Enabled = false;
                    _numInterval.Enabled = false;
                    _lblDetailStats.Text = "暂无选中客户端，请点击上方列表中任意行。";
                    return;
                }

                string charName = ctx.Session.IsAttached ? ctx.Session.GetPlayerName() : ctx.ProcessInfo.CharacterName;
                if (string.IsNullOrWhiteSpace(charName))
                {
                    charName = ctx.Session.GameState == ClientGameState.CharSelect ? "未选角(等待进入游戏)" : "未选角色";
                }

                _lblSelectedAccount.Text = $"当前选中: PID {ctx.ProcessInfo.Pid} | 角色: {charName}";
                _chkAutoReconnect.Enabled = true;
                _btnBottomReconnect.Enabled = true;
                _numInterval.Enabled = true;

                _chkAutoReconnect.Checked = ctx.ReconnectCtrl.AutoReconnectEnabled;
                _numInterval.Value = Math.Clamp(ctx.ReconnectCtrl.StepIntervalSeconds, (int)_numInterval.Minimum, (int)_numInterval.Maximum);

                var el = ctx.Tracker.ElapsedTime;
                double speed = (!ctx.Tracker.IsRunning || ctx.Tracker.TotalGained <= 0) ? 0.0 : ctx.Tracker.TotalPerHour;
                _lblDetailStats.Text = $"总矿: {ctx.Tracker.TotalOreCount} | 总草: {ctx.Tracker.TotalHerbCount} | 背包空位: {ctx.Tracker.FreeSlots}/{ctx.Tracker.TotalSlots} | 本次采集: +{ctx.Tracker.TotalGained} | 计时: {el:hh\\:mm\\:ss} | 时速: {speed:F1}/h";
            }
            finally
            {
                _isSyncingUi = false;
            }
        }

        // ── 周期性超轻量刷新 (单次读 1 字节，绝不卡死 CPU) ───────────
        private void UpdateUiTick()
        {
            List<ProcessMonitorContext> snapshot;
            lock (_lock)
            {
                snapshot = _contexts.ToList();
            }

            foreach (var ctx in snapshot)
            {
                if (ctx.Session.IsAttached)
                {
                    ctx.Session.CheckWorldState();
                    if (ctx.Session.IsInWorld)
                    {
                        Task.Run(() => ctx.Tracker.PollBagCounts());
                    }
                }
            }

            SyncTableRows();
        }

        private void StartAllMonitoring()
        {
            lock (_lock)
            {
                foreach (var ctx in _contexts)
                {
                    ctx.IsMonitored = true;
                    ctx.ReconnectCtrl.AutoReconnectEnabled = true;
                    ctx.ReconnectCtrl.Start();
                    ctx.Tracker.Start();
                }
            }
            SetStatus("已对全部活动客户端开启自动重连与采集统计");
            SyncTableRows();
        }

        private void StopAllMonitoring()
        {
            lock (_lock)
            {
                foreach (var ctx in _contexts)
                {
                    ctx.IsMonitored = false;
                    ctx.ReconnectCtrl.Stop();
                    ctx.Tracker.Stop();
                }
            }
            SetStatus("已停止全部客户端自动重连与统计");
            SyncTableRows();
        }

        private void ReconnectNowSelected()
        {
            var ctx = _selectedContext;
            if (ctx != null)
            {
                ctx.IsMonitored = true;
                ctx.ReconnectCtrl.AutoReconnectEnabled = true;
                ctx.ReconnectCtrl.TriggerImmediateReconnect();
                SetStatus($"[PID {ctx.ProcessInfo.Pid}] 已触发立即重连进游戏 (默认4秒重试机制)...");
                SyncTableRows();
            }
            else
            {
                ReconnectNow();
            }
        }

        private void ReconnectNow()
        {
            lock (_lock)
            {
                var target = _selectedContext;
                if (target != null && !target.Session.IsInWorld)
                {
                    target.IsMonitored = true;
                    target.ReconnectCtrl.AutoReconnectEnabled = true;
                    target.ReconnectCtrl.TriggerImmediateReconnect();
                    SetStatus($"[PID {target.ProcessInfo.Pid}] 已触发立即重连进游戏 (默认4秒重试机制)...");
                }
                else
                {
                    var pending = _contexts.Where(c => c.Session.IsAttached && !c.Session.IsInWorld).ToList();
                    if (pending.Count > 0)
                    {
                        foreach (var ctx in pending)
                        {
                            ctx.IsMonitored = true;
                            ctx.ReconnectCtrl.AutoReconnectEnabled = true;
                            ctx.ReconnectCtrl.TriggerImmediateReconnect();
                        }
                        SetStatus($"已对 {pending.Count} 个未进世界的客户端触发立即重连 (默认4秒重试机制)...");
                    }
                    else if (target != null)
                    {
                        target.IsMonitored = true;
                        target.ReconnectCtrl.AutoReconnectEnabled = true;
                        target.ReconnectCtrl.TriggerImmediateReconnect();
                        SetStatus($"[PID {target.ProcessInfo.Pid}] 已手动触发重连进游戏...");
                    }
                    else
                    {
                        SetStatus("当前所有客户端均已在游戏内。");
                    }
                }
            }
            SyncTableRows();
        }

        private void ToggleSelectedMonitor(bool start)
        {
            var ctx = _selectedContext;
            if (ctx != null)
            {
                ctx.IsMonitored = start;
                ctx.ReconnectCtrl.AutoReconnectEnabled = start;
                if (start)
                {
                    ctx.ReconnectCtrl.Start();
                    ctx.Tracker.Start();
                    SetStatus($"客户端 PID {ctx.ProcessInfo.Pid} 启动单独监控");
                }
                else
                {
                    ctx.ReconnectCtrl.Stop();
                    ctx.Tracker.Stop();
                    SetStatus($"客户端 PID {ctx.ProcessInfo.Pid} 停止单独监控");
                }
                SyncTableRows();
            }
        }

        private void BringSelectedToFront()
        {
            var ctx = _selectedContext;
            if (ctx != null)
            {
                IntPtr hWnd = ctx.Session.GetOrRefreshWindowHandle();
                if (hWnd != IntPtr.Zero)
                {
                    BackgroundInput.ActivateWindow(hWnd);
                }
            }
        }

        private void TestSendEnter()
        {
            var ctx = _selectedContext;
            if (ctx != null)
            {
                IntPtr hWnd = ctx.Session.GetOrRefreshWindowHandle();
                if (hWnd != IntPtr.Zero)
                {
                    Task.Run(() =>
                    {
                        BackgroundInput.SendReconnectAction(hWnd, 0.50, 0.92);
                    });
                    SetStatus($"已向客户端 PID {ctx.ProcessInfo.Pid} 发送 [纯后台消息] 回车+进游戏测试");
                    return;
                }
            }
            SetStatus("未选择有效客户端或窗口句柄无效");
        }

        private void ResetGatheringStats()
        {
            var ctx = _selectedContext;
            if (ctx != null)
            {
                ctx.Tracker.Reset();
                SetStatus($"客户端 PID {ctx.ProcessInfo.Pid} 采集统计已重置");
                SyncTableRows();
            }
        }

        private void CloseSelectedProcess()
        {
            var ctx = _selectedContext;
            if (ctx == null)
            {
                MessageBox.Show("当前未选中任何游戏客户端！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string charName = ctx.Session.IsAttached ? ctx.Session.GetPlayerName() : ctx.ProcessInfo.CharacterName;
            if (string.IsNullOrWhiteSpace(charName)) charName = "未知角色";

            var dr = MessageBox.Show(
                $"确认关闭当前选中的游戏客户端？\n\nPID: {ctx.ProcessInfo.Pid} | 角色: {charName}",
                "确认关闭游戏",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (dr != DialogResult.Yes) return;

            int pid = ctx.ProcessInfo.Pid;
            try
            {
                ctx.ReconnectCtrl.Stop();
                ctx.Session.Dispose();

                var proc = Process.GetProcessById(pid);
                if (!proc.HasExited)
                {
                    proc.CloseMainWindow();
                    if (!proc.WaitForExit(1000))
                    {
                        proc.Kill();
                    }
                }
                SetStatus($"[关闭成功] 游戏客户端 PID {pid} 已关闭");
            }
            catch (Exception ex)
            {
                SetStatus($"[关闭异常] {ex.Message}");
            }

            lock (_lock)
            {
                _contexts.Remove(ctx);
            }
            _selectedContext = null;
            SyncTableRows();
        }

        private void SetStatus(string message)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => SetStatus(message)));
                return;
            }
            _status.Text = $"[{DateTime.Now:HH:mm:ss}] {message}";
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _uiTimer.Stop();
            _scanTimer.Stop();

            lock (_lock)
            {
                foreach (var ctx in _contexts)
                {
                    try
                    {
                        ctx.ReconnectCtrl.Stop();
                        ctx.Tracker.Stop();
                        ctx.Session.Dispose();
                    }
                    catch { }
                }
                _contexts.Clear();
            }

            base.OnFormClosing(e);
        }
    }
}
