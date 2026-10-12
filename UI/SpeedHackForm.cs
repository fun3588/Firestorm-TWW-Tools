using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FirestormTwwMonitor.Core.Memory;
using FirestormTwwMonitor.Core.SpeedHack;

namespace FirestormTwwMonitor.UI
{
    public class ProcessChoiceItem
    {
        public int Pid { get; set; }
        public string Title { get; set; } = string.Empty;
        public bool IsAll { get; set; } = false;

        public override string ToString() => Title;
    }

    /// <summary>
    /// 变速器控制面板（浮动窗口）。
    /// 支持指定单进程调速，或多进程批量调速。
    /// </summary>
    public class SpeedHackForm : Form
    {
        public Func<List<ProcessMonitorContext>>? GetContexts { get; set; }

        // pid → 对应的变速控制器（每个客户端独立 Hook）
        private readonly Dictionary<int, SpeedHackController> _controllers = new();

        // 目标进程选择
        private ComboBox _cmbTarget = null!;
        private TrackBar _slider = null!;
        private Label _lblSpeed = null!;
        private Button _btnApply = null!;
        private Button _btnReset = null!;
        private Button _btnUnhook = null!;
        private Button _btnRefresh = null!;
        private CheckedListBox _clbProcesses = null!;
        private Label _lblStatus = null!;
        private CheckBox _chkSyncAll = null!;

        private float CurrentSpeed => _slider.Value / 10.0f;
        private int _initialPid = 0;

        public SpeedHackForm(int initialSelectedPid = 0)
        {
            _initialPid = initialSelectedPid;
            BuildUi();
            FormClosing += (s, e) =>
            {
                // 关闭窗口时不强制还原游戏速度，用户可继续保持加速，
                // 如需还原可点“恢复原速”或“卸载Hook”
            };
        }

        private void BuildUi()
        {
            this.Text = "⚡ 游戏进程变速器 (Speed Hack) · 精确选定";
            this.Size = new Size(500, 460);
            this.MinimumSize = new Size(460, 420);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(15, 23, 42);
            this.ForeColor = Color.White;
            this.Font = new Font("Microsoft YaHei UI", 9f);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 7,
                Padding = new Padding(16, 12, 16, 12),
                BackColor = Color.Transparent
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); // 0: 目标进程下拉选择
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); // 1: 速度滑条
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); // 2: 速度文字数值
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); // 3: 快捷倍速按钮
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 4: 进程状态/勾选详情
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); // 5: 功能按钮
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); // 6: 状态栏

            // ── Row 0: 目标进程下拉选择 ──────────────────────────────────
            var targetPanel = new Panel { Dock = DockStyle.Fill };
            var lblTarget = new Label
            {
                Text = "目标进程:",
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(0, 6),
                AutoSize = true
            };
            _cmbTarget = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(72, 3),
                Width = 280,
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 9f)
            };
            _cmbTarget.SelectedIndexChanged += (s, e) => OnTargetSelectionChanged();

            _chkSyncAll = new CheckBox
            {
                Text = "批量同步",
                Location = new Point(362, 5),
                AutoSize = true,
                ForeColor = Color.FromArgb(203, 213, 225)
            };
            _chkSyncAll.CheckedChanged += (s, e) =>
            {
                if (_chkSyncAll.Checked && _cmbTarget.Items.Count > 0)
                {
                    _cmbTarget.SelectedIndex = 0; // 选“全部客户端”
                }
            };

            targetPanel.Controls.AddRange(new Control[] { lblTarget, _cmbTarget, _chkSyncAll });
            layout.Controls.Add(targetPanel, 0, 0);

            // ── Row 1: 速度滑条 (0.1x ~ 10.0x) ───────────────────────────
            var sliderPanel = new Panel { Dock = DockStyle.Fill };
            var lblMin = new Label { Text = "0.1x", Location = new Point(0, 14), ForeColor = Color.FromArgb(148, 163, 184), AutoSize = true };
            var lblMax = new Label { Text = "10.0x", AutoSize = true, ForeColor = Color.FromArgb(148, 163, 184) };

            _slider = new TrackBar
            {
                Minimum = 1,    // 0.1x
                Maximum = 100,  // 10.0x
                Value = 10,     // 1.0x
                TickFrequency = 10,
                LargeChange = 5,
                SmallChange = 1,
                Width = 390,
                Location = new Point(32, 6)
            };
            _slider.BackColor = Color.FromArgb(15, 23, 42);
            lblMax.Location = new Point(_slider.Right + 4, 14);

            _slider.ValueChanged += (s, e) => UpdateSpeedLabel();

            sliderPanel.Controls.AddRange(new Control[] { lblMin, _slider, lblMax });
            layout.Controls.Add(sliderPanel, 0, 1);

            // ── Row 2: 当前速度显示 ──────────────────────────────────────
            _lblSpeed = new Label
            {
                Text = "当前倍率：1.0x  （原速）",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold),
                ForeColor = Color.FromArgb(96, 165, 250)
            };
            layout.Controls.Add(_lblSpeed, 0, 2);

            // ── Row 3: 快捷预设按钮 (0.5x, 1.0x, 1.5x, 2.0x, 3.0x, 5.0x) ─
            var quickPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight
            };
            var quickSpeeds = new (string text, int val)[]
            {
                ("0.5x 减速", 5),
                ("1.0x 原速", 10),
                ("1.5x", 15),
                ("2.0x 双倍", 20),
                ("3.0x", 30),
                ("5.0x 极速", 50)
            };
            foreach (var q in quickSpeeds)
            {
                var qBtn = new Button
                {
                    Text = q.text,
                    AutoSize = true,
                    BackColor = Color.FromArgb(30, 41, 59),
                    ForeColor = Color.FromArgb(226, 232, 240),
                    FlatStyle = FlatStyle.Flat,
                    Margin = new Padding(0, 0, 6, 0),
                    Font = new Font("Microsoft YaHei UI", 8f),
                    Height = 26
                };
                qBtn.FlatAppearance.BorderSize = 1;
                qBtn.FlatAppearance.BorderColor = Color.FromArgb(71, 85, 105);
                int targetVal = q.val;
                qBtn.Click += (s, e) =>
                {
                    _slider.Value = targetVal;
                };
                quickPanel.Controls.Add(qBtn);
            }
            layout.Controls.Add(quickPanel, 0, 3);

            // ── Row 4: 进程状态/勾选详情 ─────────────────────────────────
            var listContainer = new Panel { Dock = DockStyle.Fill };
            var lblListTitle = new Label
            {
                Text = "客户端多选列表（勾选的进程均会接收变速指令）：",
                Dock = DockStyle.Top,
                Height = 20,
                ForeColor = Color.FromArgb(148, 163, 184),
                Font = new Font("Microsoft YaHei UI", 8.5f)
            };
            _clbProcesses = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 9f),
                CheckOnClick = true,
                BorderStyle = BorderStyle.FixedSingle
            };
            _clbProcesses.SelectedIndexChanged += (s, e) =>
            {
                if (_clbProcesses.SelectedItem is ProcessChoiceItem item && !item.IsAll)
                {
                    // 在下拉框中同步定位
                    for (int i = 0; i < _cmbTarget.Items.Count; i++)
                    {
                        if (_cmbTarget.Items[i] is ProcessChoiceItem pi && pi.Pid == item.Pid)
                        {
                            _cmbTarget.SelectedIndex = i;
                            break;
                        }
                    }
                }
            };
            listContainer.Controls.Add(_clbProcesses);
            listContainer.Controls.Add(lblListTitle);
            layout.Controls.Add(listContainer, 0, 4);

            // ── Row 5: 功能按钮 ─────────────────────────────────────────
            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight
            };

            _btnApply = MakeButton("⚡ 应用到选定进程", Color.FromArgb(37, 99, 235));
            _btnApply.Click += (s, e) => ApplySpeed();
            btnPanel.Controls.Add(_btnApply);

            _btnReset = MakeButton("↺ 恢复原速 (1.0x)", Color.FromArgb(71, 85, 105));
            _btnReset.Click += (s, e) =>
            {
                _slider.Value = 10;
                ApplySpeed();
            };
            btnPanel.Controls.Add(_btnReset);

            _btnUnhook = MakeButton("🔓 卸载Hook", Color.FromArgb(185, 28, 28));
            _btnUnhook.Click += (s, e) => UnhookTarget();
            btnPanel.Controls.Add(_btnUnhook);

            _btnRefresh = MakeButton("🔍 刷新进程", Color.FromArgb(5, 150, 105));
            _btnRefresh.Click += (s, e) => RefreshProcessList();
            btnPanel.Controls.Add(_btnRefresh);

            layout.Controls.Add(btnPanel, 0, 5);

            // ── Row 6: 状态栏 ───────────────────────────────────────────
            _lblStatus = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(148, 163, 184),
                Font = new Font("Microsoft YaHei UI", 8.5f),
                Text = "就绪。请选择目标进程并调节变速倍率。"
            };
            layout.Controls.Add(_lblStatus, 0, 6);

            this.Controls.Add(layout);
            UpdateSpeedLabel();
        }

        private void OnTargetSelectionChanged()
        {
            if (_cmbTarget.SelectedItem is ProcessChoiceItem item)
            {
                if (item.IsAll)
                {
                    _btnApply.Text = "⚡ 应用到全部客户端";
                    _btnUnhook.Text = "🔓 卸载全部 Hook";
                }
                else
                {
                    _btnApply.Text = $"⚡ 应用到 PID {item.Pid}";
                    _btnUnhook.Text = $"🔓 卸载 PID {item.Pid}";

                    // 读取该进程当前速度
                    if (_controllers.TryGetValue(item.Pid, out var ctrl) && ctrl.IsInstalled)
                    {
                        int sliderVal = Math.Clamp((int)Math.Round(ctrl.CurrentSpeed * 10f), 1, 100);
                        _slider.Value = sliderVal;
                        SetStatus($"当前目标 PID {item.Pid}，已安装 Hook，当前倍率: {ctrl.CurrentSpeed:F1}x");
                    }
                }
            }
        }

        public void RefreshProcessList()
        {
            var contexts = GetContexts?.Invoke() ?? new List<ProcessMonitorContext>();

            _cmbTarget.Items.Clear();
            _clbProcesses.Items.Clear();

            // 全部选项
            var allItem = new ProcessChoiceItem
            {
                Pid = 0,
                IsAll = true,
                Title = $"🌟 [全部客户端批量] ({contexts.Count} 个)"
            };
            _cmbTarget.Items.Add(allItem);

            int targetIndex = 0;

            for (int i = 0; i < contexts.Count; i++)
            {
                var ctx = contexts[i];
                string charName = ctx.Session.IsAttached ? ctx.Session.GetPlayerName() : ctx.ProcessInfo.CharacterName;
                if (string.IsNullOrWhiteSpace(charName)) charName = "未选角";

                string hookState = "";
                if (_controllers.TryGetValue(ctx.ProcessInfo.Pid, out var ctrl) && ctrl.IsInstalled)
                {
                    hookState = $" [{ctrl.CurrentSpeed:F1}x 加速中]";
                }

                var item = new ProcessChoiceItem
                {
                    Pid = ctx.ProcessInfo.Pid,
                    IsAll = false,
                    Title = $"PID {ctx.ProcessInfo.Pid}  |  {charName}{hookState}"
                };

                _cmbTarget.Items.Add(item);
                _clbProcesses.Items.Add(item, true); // 默认列表全选

                if (_initialPid > 0 && ctx.ProcessInfo.Pid == _initialPid)
                {
                    targetIndex = _cmbTarget.Items.Count - 1;
                }
            }

            if (_cmbTarget.Items.Count > 0)
            {
                _cmbTarget.SelectedIndex = targetIndex;
            }

            SetStatus($"已扫描到 {contexts.Count} 个活动客户端进程。");
        }

        private void ApplySpeed()
        {
            var contexts = GetContexts?.Invoke() ?? new List<ProcessMonitorContext>();
            if (contexts.Count == 0)
            {
                SetStatus("❌ 未找到活动客户端进程！");
                return;
            }

            float speed = CurrentSpeed;
            var targetItem = _cmbTarget.SelectedItem as ProcessChoiceItem;

            var targets = new List<ProcessMonitorContext>();

            if (targetItem != null && !targetItem.IsAll)
            {
                // 单个指定进程
                var single = contexts.FirstOrDefault(c => c.ProcessInfo.Pid == targetItem.Pid);
                if (single != null) targets.Add(single);
            }
            else
            {
                // 批量选中的进程（按 CheckBox 勾选）
                var checkedPids = new HashSet<int>();
                foreach (var obj in _clbProcesses.CheckedItems)
                {
                    if (obj is ProcessChoiceItem pi && !pi.IsAll)
                    {
                        checkedPids.Add(pi.Pid);
                    }
                }

                targets = contexts.Where(c => checkedPids.Contains(c.ProcessInfo.Pid)).ToList();
                if (targets.Count == 0)
                {
                    targets = contexts.ToList(); // 若都未勾选，默认全部
                }
            }

            int success = 0, fail = 0;

            foreach (var ctx in targets)
            {
                if (!ctx.Session.IsAttached) continue;
                int pid = ctx.ProcessInfo.Pid;

                if (!_controllers.TryGetValue(pid, out var ctrl))
                {
                    ctrl = new SpeedHackController(ctx.Session);
                    _controllers[pid] = ctrl;
                }

                bool ok = ctrl.IsInstalled ? ctrl.SetSpeed(speed) : ctrl.Install(speed);
                if (ok) success++; else fail++;
            }

            SetStatus(fail == 0
                ? $"✅ 已将 {speed:F1}x 变速生效至 {success} 个客户端！"
                : $"⚠️ 成功 {success} 个，失败 {fail} 个。");

            // 重新刷新显示状态文本
            UpdateItemTitles(contexts);
        }

        private void UnhookTarget()
        {
            var contexts = GetContexts?.Invoke() ?? new List<ProcessMonitorContext>();
            var targetItem = _cmbTarget.SelectedItem as ProcessChoiceItem;

            if (targetItem != null && !targetItem.IsAll)
            {
                if (_controllers.TryGetValue(targetItem.Pid, out var ctrl))
                {
                    ctrl.Uninstall();
                    _controllers.Remove(targetItem.Pid);
                    SetStatus($"✅ PID {targetItem.Pid} 已卸载 Hook，恢复原速。");
                }
            }
            else
            {
                int count = 0;
                foreach (var ctrl in _controllers.Values)
                {
                    ctrl.Uninstall();
                    count++;
                }
                _controllers.Clear();
                SetStatus($"✅ 已卸载全部 {count} 个客户端的变速 Hook，恢复原速。");
            }

            UpdateItemTitles(contexts);
        }

        private void UpdateItemTitles(List<ProcessMonitorContext> contexts)
        {
            for (int i = 0; i < _cmbTarget.Items.Count; i++)
            {
                if (_cmbTarget.Items[i] is ProcessChoiceItem item && !item.IsAll)
                {
                    var ctx = contexts.FirstOrDefault(c => c.ProcessInfo.Pid == item.Pid);
                    string charName = ctx?.Session.IsAttached == true ? ctx.Session.GetPlayerName() : ctx?.ProcessInfo.CharacterName ?? "";
                    if (string.IsNullOrWhiteSpace(charName)) charName = "未选角";

                    string hookState = "";
                    if (_controllers.TryGetValue(item.Pid, out var ctrl) && ctrl.IsInstalled)
                    {
                        hookState = $" [{ctrl.CurrentSpeed:F1}x 加速中]";
                    }
                    item.Title = $"PID {item.Pid}  |  {charName}{hookState}";
                }
            }
            _cmbTarget.Invalidate();
            _clbProcesses.Invalidate();
        }

        private void UpdateSpeedLabel()
        {
            float s = CurrentSpeed;
            string desc = s switch
            {
                var v when v < 0.95f => $"{v:F1}x  （减速）",
                var v when v > 1.05f => $"{v:F1}x  （加速）",
                _ => "1.0x  （原速）"
            };

            Color clr = s switch
            {
                var v when v < 0.95f => Color.FromArgb(251, 191, 36),
                var v when v > 1.05f => Color.FromArgb(52, 211, 153),
                _ => Color.FromArgb(96, 165, 250)
            };

            _lblSpeed.Text = $"当前倍率：{desc}";
            _lblSpeed.ForeColor = clr;
        }

        private void SetStatus(string msg)
        {
            if (InvokeRequired) { BeginInvoke(() => _lblStatus.Text = msg); return; }
            _lblStatus.Text = msg;
        }

        private static Button MakeButton(string text, Color bg)
        {
            return new Button
            {
                Text = text,
                BackColor = bg,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                AutoSize = true,
                MinimumSize = new Size(60, 30),
                Margin = new Padding(0, 4, 8, 4),
                Font = new Font("Microsoft YaHei UI", 8.5f)
            };
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
        }
    }
}
