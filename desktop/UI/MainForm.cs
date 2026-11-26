using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using DroidDeck.Core;
using DroidDeck.Models;

namespace DroidDeck.UI
{
    public class MainForm : Form
    {
        private readonly AdbManager _adb;
        private readonly ScrcpyControlChannel _control;
        private readonly ScrcpyHost _host;
        private readonly InputEngine _input;
        private readonly OverlayForm _overlay;
        private KeymapStoreData _store;

        // UI Controls
        private Panel _sidebar;
        private Panel _stagePanel;
        private Panel _videoHostPanel;
        private Panel _standbyPanel;
        private Label _lblConnBadge;
        private Label _lblModel;
        private Label _lblOs;
        private Label _lblRes;
        private Label _lblBat;
        private Label _lblStandbyTitle;
        private Label _lblStandbyDesc;
        private Label _lblHudState;
        private Label _lblHudRes;
        private Label _lblHudFps;
        private Label _lblHudMode;

        private ComboBox _cmbMaxSize;
        private ComboBox _cmbMaxFps;
        private ComboBox _cmbBitRate;
        private CheckBox _chkAudio;
        private CheckBox _chkTurnScreenOff;
        private CheckBox _chkAntiAlias;
        private Button _btnStart;
        private Button _btnStop;
        private Button _btnHeroStart;
        private Button _btnToggleSidebar;
        private Button _btnFullscreen;
        private bool _isFullscreen;
        private FormWindowState _prevWindowState;
        private Rectangle _prevBounds;
        private bool _prevSidebarVisible = true;

        private Button _btnModeTouch;
        private Button _btnModePlay;
        private Button _btnModeEdit;
        private CheckBox _chkShowOverlay;
        private ComboBox _cmbPreset;
        private TrackBar _trkSens;
        private TrackBar _trkJoyRadius;
        private Panel _editPanel;
        private Label _lblPlayHint;

        private Timer _pollTimer;
        private bool _isStarting;
        private readonly List<string> _presetKeys = new List<string>();
        private bool _updatingPresetCombo;

        public MainForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            Text = "DroidDeck — Type-C 原生 120FPS 极速投屏 & 键鼠映射软件";
            Size = new Size(1340, 860);
            MinimumSize = new Size(1060, 720);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(11, 14, 20);
            ForeColor = Color.FromArgb(241, 245, 249);
            Font = new Font("Microsoft YaHei UI", 9f);

            _store = KeymapRepository.Load();
            _adb = new AdbManager();
            _control = new ScrcpyControlChannel(_adb);

            BuildLayout();

            _host = new ScrcpyHost(_adb, _videoHostPanel);
            _input = new InputEngine(this, _host, _control, GetVideoScreenRect, () => _overlay);
            _overlay = new OverlayForm(this, _input);

            BindEvents();
            ApplyPresetToUI(_store.ActivePresetKey);

            Shown += async (s, e) =>
            {
                SyncOverlay();
                await _adb.EnsureAdbReadyAsync();
                await RefreshStatusAsync();
                _pollTimer.Start();
                var st = _adb.CurrentStatus;
                if (st.AdbState == "device" && !string.IsNullOrEmpty(st.Serial) && !_host.IsRunning)
                {
                    await StartMirroringAsync();
                }
            };
        }

        protected override void WndProc(ref Message m)
        {
            if (_input != null && m.Msg == NativeMethods.WM_INPUT)
            {
                _input.ProcessRawInputMessage(ref m);
            }
            base.WndProc(ref m);
        }

        private Rectangle GetVideoScreenRect()
        {
            if (_videoHostPanel == null || _videoHostPanel.IsDisposed) return Rectangle.Empty;
            if (_host != null && _host.EmbeddedHwnd != IntPtr.Zero && NativeMethods.IsWindow(_host.EmbeddedHwnd))
            {
                NativeMethods.RECT r;
                if (NativeMethods.GetWindowRect(_host.EmbeddedHwnd, out r) && (r.Right - r.Left) > 10 && (r.Bottom - r.Top) > 10)
                {
                    return new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
                }
            }
            Rectangle fitted = _host.GetFittedVideoRect();
            Point topLeft = _videoHostPanel.PointToScreen(new Point(fitted.X, fitted.Y));
            return new Rectangle(topLeft.X, topLeft.Y, fitted.Width, fitted.Height);
        }

        private void SyncOverlay()
        {
            if (_overlay == null || _overlay.IsDisposed) return;
            if (!_host.IsRunning)
            {
                _overlay.Hide();
                return;
            }
            _overlay.SyncBounds(GetVideoScreenRect());
        }

        private void BuildLayout()
        {
            _sidebar = new Panel
            {
                Dock = DockStyle.Left,
                Width = 390,
                BackColor = Color.FromArgb(19, 24, 34),
                AutoScroll = true,
                Padding = new Padding(12)
            };

            _stagePanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(9, 12, 18)
            };

            // Top HUD bar on right stage
            var hudBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = Color.FromArgb(19, 24, 34),
                Padding = new Padding(10, 7, 10, 7)
            };

            var hudFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };

            _btnToggleSidebar = new Button
            {
                Text = "◀ 收起侧栏",
                Size = new Size(96, 28),
                Margin = new Padding(0, 1, 8, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(26, 33, 48),
                ForeColor = Color.FromArgb(226, 232, 240),
                Font = new Font("Microsoft YaHei UI", 8.8f),
                Cursor = Cursors.Hand
            };
            _lblHudState = MakeHudPill("⏹ 待启动投屏", 0);
            _lblHudRes = MakeHudPill("分辨率: --", 0);
            _lblHudFps = MakeHudPill("帧率: 0 FPS", 0);
            _lblHudMode = MakeHudPill("模式: 🖱️ 鼠标触控", 0);
            _lblHudMode.ForeColor = Color.FromArgb(255, 176, 116);
            foreach (var p in new[] { _lblHudState, _lblHudRes, _lblHudFps, _lblHudMode })
            {
                p.Margin = new Padding(0, 2, 8, 0);
            }

            hudFlow.Controls.Add(_btnToggleSidebar);
            hudFlow.Controls.Add(_lblHudState);
            hudFlow.Controls.Add(_lblHudRes);
            hudFlow.Controls.Add(_lblHudFps);
            hudFlow.Controls.Add(_lblHudMode);

            _btnFullscreen = new Button
            {
                Text = "⛶ 全屏 (F11)",
                Dock = DockStyle.Right,
                Width = 110,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(37, 47, 68),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 8.8f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };

            hudBar.Controls.Add(hudFlow);
            hudBar.Controls.Add(_btnFullscreen);

            _videoHostPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black
            };

            // Standby Overlay Panel inside _videoHostPanel
            _standbyPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(11, 14, 20)
            };
            var standbyCard = new Panel
            {
                Size = new Size(420, 260),
                BackColor = Color.FromArgb(26, 33, 48),
                Padding = new Padding(20)
            };
            var lblPhoneEmoji = new Label
            {
                Text = "📱",
                Font = new Font("Segoe UI Emoji", 28f),
                Dock = DockStyle.Top,
                Height = 56,
                TextAlign = ContentAlignment.MiddleCenter
            };
            _lblStandbyTitle = new Label
            {
                Text = "准备连接手机 (Type-C 直连)",
                Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 32,
                TextAlign = ContentAlignment.MiddleCenter
            };
            _lblStandbyDesc = new Label
            {
                Text = "请用 Type-C 数据线连接小米 / Android 手机，并开启「USB 调试」与「USB 调试（安全设置）」。",
                Dock = DockStyle.Top,
                Height = 64,
                ForeColor = Color.FromArgb(148, 163, 184),
                TextAlign = ContentAlignment.MiddleCenter
            };
            _btnHeroStart = MakePrimaryButton("▶ 启动 120FPS 原生桌面投屏", 60, 185, 296, 40);
            standbyCard.Controls.Add(_btnHeroStart);
            standbyCard.Controls.Add(_lblStandbyDesc);
            standbyCard.Controls.Add(_lblStandbyTitle);
            standbyCard.Controls.Add(lblPhoneEmoji);
            _standbyPanel.Controls.Add(standbyCard);
            _standbyPanel.Resize += (s, e) =>
            {
                standbyCard.Location = new Point(
                    Math.Max(10, (_standbyPanel.Width - standbyCard.Width) / 2),
                    Math.Max(10, (_standbyPanel.Height - standbyCard.Height) / 2)
                );
            };

            _videoHostPanel.Controls.Add(_standbyPanel);
            _stagePanel.Controls.Add(_videoHostPanel);
            _stagePanel.Controls.Add(hudBar);

            BuildSidebarCards();

            Controls.Add(_stagePanel);
            Controls.Add(_sidebar);
        }

        private void BuildSidebarCards()
        {
            int y = 10;

            // Header
            var lblLogo = new Label
            {
                Text = "DD",
                Location = new Point(12, y),
                Size = new Size(42, 42),
                BackColor = Color.FromArgb(255, 105, 0),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };
            var lblTitle = new Label
            {
                Text = "DroidDeck 原生桌面端",
                Location = new Point(62, y),
                Size = new Size(290, 22),
                Font = new Font("Microsoft YaHei UI", 11.5f, FontStyle.Bold)
            };
            var lblSub = new Label
            {
                Text = "D3D11VA 120FPS + 音频 + RawInput 键鼠映射",
                Location = new Point(62, y + 23),
                Size = new Size(300, 18),
                ForeColor = Color.FromArgb(148, 163, 184),
                Font = new Font("Microsoft YaHei UI", 8.2f)
            };
            _sidebar.Controls.Add(lblLogo);
            _sidebar.Controls.Add(lblTitle);
            _sidebar.Controls.Add(lblSub);
            y += 54;

            // Card 1: Connection Status
            var cardConn = MakeCard(12, y, 348, 178, "Type-C 连接状态");
            _lblConnBadge = new Label
            {
                Text = "检测中...",
                Location = new Point(216, 10),
                Size = new Size(120, 22),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(65, 50, 18),
                ForeColor = Color.FromArgb(251, 191, 36),
                Font = new Font("Microsoft YaHei UI", 8f, FontStyle.Bold)
            };
            cardConn.Controls.Add(_lblConnBadge);
            _lblModel = AddInfoField(cardConn, "手机型号", "未连接", 12, 42);
            _lblOs = AddInfoField(cardConn, "系统版本", "--", 180, 42);
            _lblRes = AddInfoField(cardConn, "物理分辨率", "--", 12, 82);
            _lblBat = AddInfoField(cardConn, "电池电量", "--", 180, 82);
            var lblGuide = new Label
            {
                Text = "⚡ 首次插线：手机开「USB 调试」+「USB 调试（安全设置）」",
                Location = new Point(10, 126),
                Size = new Size(326, 42),
                ForeColor = Color.FromArgb(255, 176, 116),
                Font = new Font("Microsoft YaHei UI", 8f)
            };
            cardConn.Controls.Add(lblGuide);
            _sidebar.Controls.Add(cardConn);
            y += 188;

            // Card 2: Stream Quality & Start/Stop
            var cardStream = MakeCard(12, y, 348, 218, "原生 120FPS 引擎设置");
            _cmbMaxSize = AddCombo(cardStream, "分辨率", 12, 36, 155, new[] { "720P 极速 (1280)", "1080P 高清 (1920)", "原生超清 (不缩放)" }, 1);
            _cmbMaxFps = AddCombo(cardStream, "最大帧率", 180, 36, 155, new[] { "60 FPS 稳帧", "90 FPS 高刷", "120 FPS 电竞满帧" }, 2);
            _cmbBitRate = AddCombo(cardStream, "视频码率", 12, 86, 155, new[] { "8 Mbps", "16 Mbps (推荐)", "24 Mbps (超清)" }, 1);
            _chkAudio = new CheckBox
            {
                Text = "同步手机声音到电脑",
                Checked = true,
                Location = new Point(180, 92),
                Size = new Size(160, 22)
            };
            _chkTurnScreenOff = new CheckBox
            {
                Text = "启动时自动熄屏省电",
                Checked = false,
                Location = new Point(180, 114),
                Size = new Size(160, 22)
            };
            _chkAntiAlias = new CheckBox
            {
                Text = "✨ 抗锯齿超清渲染 (OpenGL 三线性过滤，消文字锯齿)",
                Checked = true,
                Location = new Point(12, 140),
                Size = new Size(326, 22),
                ForeColor = Color.FromArgb(167, 243, 208)
            };
            _btnStart = MakePrimaryButton("▶ 启动 120FPS 原生投屏", 12, 168, 324, 38);
            _btnStop = new Button
            {
                Text = "■ 停止投屏",
                Location = new Point(12, 168),
                Size = new Size(324, 38),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(220, 38, 38),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
                Visible = false,
                Cursor = Cursors.Hand
            };
            cardStream.Controls.Add(_chkAudio);
            cardStream.Controls.Add(_chkTurnScreenOff);
            cardStream.Controls.Add(_chkAntiAlias);
            cardStream.Controls.Add(_btnStart);
            cardStream.Controls.Add(_btnStop);
            _sidebar.Controls.Add(cardStream);
            y += 228;

            // Card 3: Quick Actions
            var cardQuick = MakeCard(12, y, 348, 134, "快捷设备控制");
            AddQuickBtn(cardQuick, "◀ 返回", 10, 36, 76, () => _control.InjectTapKeycode(4));
            AddQuickBtn(cardQuick, "● 桌面", 92, 36, 76, () => _control.InjectTapKeycode(3));
            AddQuickBtn(cardQuick, "■ 后台", 174, 36, 76, () => _control.InjectTapKeycode(187));
            AddQuickBtn(cardQuick, "⏻ 电源", 256, 36, 80, () => _control.InjectTapKeycode(26));
            AddQuickBtn(cardQuick, "🔊 音量+", 10, 68, 76, () => _control.InjectTapKeycode(24));
            AddQuickBtn(cardQuick, "🔉 音量-", 92, 68, 76, () => _control.InjectTapKeycode(25));
            AddQuickBtn(cardQuick, "🌙 熄屏", 174, 68, 76, () => _control.SetDisplayPower(false));
            AddQuickBtn(cardQuick, "☀ 亮屏", 256, 68, 80, () => _control.SetDisplayPower(true));
            AddQuickBtn(cardQuick, "📸 原画截图", 10, 100, 158, () => _ = TakeScreenshotAsync());
            AddQuickBtn(cardQuick, "🔄 重置 ADB", 174, 100, 162, () => _ = ResetAdbAsync());
            _sidebar.Controls.Add(cardQuick);
            y += 144;

            // Card 4: Keymapper
            var cardKey = MakeCard(12, y, 348, 300, "游戏键鼠映射 (RawInput + 多指并发)");
            _chkShowOverlay = new CheckBox
            {
                Text = "显示键位提示",
                Checked = true,
                Location = new Point(225, 10),
                Size = new Size(115, 22)
            };
            cardKey.Controls.Add(_chkShowOverlay);

            _btnModeTouch = MakeModeBtn("🖱️ 鼠标触控", 10, 38, 104, true);
            _btnModePlay = MakeModeBtn("🎮 键鼠激活", 120, 38, 104, false);
            _btnModeEdit = MakeModeBtn("🛠️ 编辑键位", 230, 38, 106, false);
            cardKey.Controls.Add(_btnModeTouch);
            cardKey.Controls.Add(_btnModePlay);
            cardKey.Controls.Add(_btnModeEdit);

            _cmbPreset = AddCombo(cardKey, "键鼠映射方案（支持自定义新建/改名/删）", 10, 74, 186, new[] { "加载中..." }, 0);
            AddQuickBtn(cardKey, "+ 新建", 202, 90, 58, CreateCustomPreset);
            AddQuickBtn(cardKey, "改名", 264, 90, 42, RenameCurrentPreset);
            AddQuickBtn(cardKey, "删", 310, 90, 26, DeleteCurrentPreset);

            var lblSens = new Label { Text = "视角灵敏度", Location = new Point(10, 128), Size = new Size(140, 18), ForeColor = Color.FromArgb(148, 163, 184) };
            _trkSens = new TrackBar { Minimum = 4, Maximum = 30, Value = 12, TickStyle = TickStyle.None, Location = new Point(8, 146), Size = new Size(158, 30) };
            var lblRad = new Label { Text = "摇杆半径", Location = new Point(178, 128), Size = new Size(140, 18), ForeColor = Color.FromArgb(148, 163, 184) };
            _trkJoyRadius = new TrackBar { Minimum = 6, Maximum = 22, Value = 12, TickStyle = TickStyle.None, Location = new Point(176, 146), Size = new Size(160, 30) };
            cardKey.Controls.Add(lblSens);
            cardKey.Controls.Add(_trkSens);
            cardKey.Controls.Add(lblRad);
            cardKey.Controls.Add(_trkJoyRadius);

            _lblPlayHint = new Label
            {
                Text = "🎯 键鼠映射提示：\n• WASD：8方向虚拟摇杆 · 支持 Tab/Space/字母键并发\n• ~ (波浪键)：锁定/解锁 360° 视角 (Esc 也可解锁)\n• 解锁后鼠标左键/滚轮自动恢复直点与滑动，不与开火冲突",
                Location = new Point(10, 184),
                Size = new Size(326, 98),
                BackColor = Color.FromArgb(15, 20, 29),
                ForeColor = Color.FromArgb(203, 213, 225),
                Padding = new Padding(8),
                Visible = false
            };

            _editPanel = new Panel
            {
                Location = new Point(10, 184),
                Size = new Size(326, 106),
                BackColor = Color.FromArgb(15, 20, 29),
                Visible = false
            };
            AddQuickBtn(_editPanel, "+ 添加按键", 6, 8, 98, AddTapKey);
            AddQuickBtn(_editPanel, "+ WASD摇杆", 112, 8, 100, AddJoystick);
            AddQuickBtn(_editPanel, "+ 准星视角", 218, 8, 100, AddAimZone);
            AddQuickBtn(_editPanel, "↺ 恢复预设", 6, 40, 100, ResetPreset);
            var btnSave = MakePrimaryButton("💾 保存键鼠映射方案", 112, 40, 206, 30);
            btnSave.Click += (s, e) =>
            {
                KeymapRepository.Save(_store);
                MessageBox.Show("键鼠映射方案已保存到本地！", "DroidDeck", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            var lblEditTip = new Label
            {
                Text = "拖拽调位 · 双击空白加键 · 单击改键/坐标 · 右键删除",
                Location = new Point(6, 78),
                Size = new Size(314, 20),
                ForeColor = Color.FromArgb(148, 163, 184),
                Font = new Font("Microsoft YaHei UI", 7.8f)
            };
            _editPanel.Controls.Add(btnSave);
            _editPanel.Controls.Add(lblEditTip);

            cardKey.Controls.Add(_lblPlayHint);
            cardKey.Controls.Add(_editPanel);
            _sidebar.Controls.Add(cardKey);
        }

        private void BindEvents()
        {
            _btnStart.Click += async (s, e) => await StartMirroringAsync();
            _btnHeroStart.Click += async (s, e) => await StartMirroringAsync();
            _btnStop.Click += (s, e) => StopMirroring();
            _btnToggleSidebar.Click += (s, e) => ToggleSidebar();
            _btnFullscreen.Click += (s, e) => ToggleFullscreen();
            _input.ToggleFullscreenRequested += ToggleFullscreen;

            _host.VideoDimensionsChanged += (w, h) =>
            {
                _lblHudRes.Text = $"分辨率: {w}×{h}";
                SyncOverlay();
            };
            _host.FpsUpdated += (fps) =>
            {
                if (IsHandleCreated) BeginInvoke(new Action(() => _lblHudFps.Text = $"实时帧率: {fps} FPS"));
            };
            _host.StatusTextChanged += (txt) =>
            {
                if (IsHandleCreated) BeginInvoke(new Action(() => _lblHudState.Text = txt));
            };
            _host.RunningStateChanged += (running) =>
            {
                if (!IsHandleCreated) return;
                BeginInvoke(new Action(() =>
                {
                    _standbyPanel.Visible = !running;
                    _btnStart.Visible = !running;
                    _btnStop.Visible = running;
                    SyncOverlay();
                }));
            };

            _control.Disconnected += () =>
            {
                if (_host.WantRunning && IsHandleCreated)
                {
                    BeginInvoke(new Action(async () =>
                    {
                        await Task.Delay(1500);
                        var st = _adb.CurrentStatus;
                        if (_host.WantRunning && st.AdbState == "device" && !string.IsNullOrEmpty(st.Serial))
                        {
                            try { await _control.StartAsync(st.Serial); } catch { }
                        }
                    }));
                }
            };

            _btnModeTouch.Click += (s, e) => SetControlMode(ControlMode.Touch);
            _btnModePlay.Click += (s, e) => SetControlMode(ControlMode.Play);
            _btnModeEdit.Click += (s, e) => SetControlMode(ControlMode.Edit);

            _chkShowOverlay.CheckedChanged += (s, e) =>
            {
                _overlay.ShowOverlay = _chkShowOverlay.Checked;
                SyncOverlay();
            };
            _cmbPreset.SelectedIndexChanged += (s, e) =>
            {
                if (_updatingPresetCombo) return;
                int idx = _cmbPreset.SelectedIndex;
                if (idx >= 0 && idx < _presetKeys.Count)
                {
                    ApplyPresetToUI(_presetKeys[idx]);
                }
            };
            _trkSens.Scroll += (s, e) =>
            {
                _store.MouseSensitivity = _trkSens.Value / 10.0;
                _input.MouseSensitivity = _store.MouseSensitivity;
            };
            _trkJoyRadius.Scroll += (s, e) =>
            {
                var joy = _input.ActivePreset?.Items.Find(i => i.Type == "joystick");
                if (joy != null)
                {
                    joy.Radius = _trkJoyRadius.Value / 100.0;
                    _overlay.RefreshOverlay();
                }
            };

            _input.OverlayVisualChanged += () =>
            {
                if (_overlay != null && !_overlay.IsDisposed)
                {
                    if (_overlay.InvokeRequired) _overlay.BeginInvoke(new Action(() => _overlay.RefreshOverlay()));
                    else _overlay.RefreshOverlay();
                }
            };
            _input.AimLockChanged += (locked) =>
            {
                _lblHudMode.Text = locked ? "模式: 🎯 视角已锁定 (Esc解锁)" : "模式: 🎮 键鼠映射激活";
            };
            _input.EditItemClicked += (item) =>
            {
                using (var dlg = new KeyBindDialog(item, _input, () => _overlay.RefreshOverlay()))
                {
                    var res = dlg.ShowDialog(this);
                    if (res == DialogResult.Abort)
                    {
                        _input.ActivePreset?.Items.Remove(item);
                        _overlay.RefreshOverlay();
                    }
                    KeymapRepository.Save(_store);
                }
            };
            _input.EditItemDeleted += (item) =>
            {
                _input.ActivePreset?.Items.Remove(item);
                _overlay.RefreshOverlay();
                KeymapRepository.Save(_store);
            };
            _input.EditEmptyDoubleClicked += (normPt) => AddTapKeyAt(normPt.X, normPt.Y);

            Move += (s, e) => SyncOverlay();
            Resize += (s, e) =>
            {
                _host.LayoutEmbeddedWindow();
                SyncOverlay();
            };
            _videoHostPanel.Resize += (s, e) =>
            {
                _host.LayoutEmbeddedWindow();
                SyncOverlay();
            };
            _videoHostPanel.MouseDown += (s, e) =>
            {
                _videoHostPanel.Focus();
                _input.HandleSurfaceMouseDown(e.Button, _videoHostPanel.PointToScreen(e.Location));
            };
            _videoHostPanel.MouseMove += (s, e) =>
            {
                _input.HandleSurfaceMouseMove(_videoHostPanel.PointToScreen(e.Location));
            };
            _videoHostPanel.MouseUp += (s, e) =>
            {
                _input.HandleSurfaceMouseUp(e.Button, _videoHostPanel.PointToScreen(e.Location));
            };
            _videoHostPanel.MouseWheel += (s, e) =>
            {
                _input.HandleSurfaceMouseWheel(e.Delta, _videoHostPanel.PointToScreen(e.Location));
            };
            Deactivate += (s, e) =>
            {
                if (IsHandleCreated && !IsDisposed)
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (Form.ActiveForm != this && Form.ActiveForm != _overlay)
                            _input.ReleaseAllInputs();
                    }));
                }
            };

            _pollTimer = new Timer { Interval = 2000 };
            _pollTimer.Tick += async (s, e) => await RefreshStatusAsync();

            FormClosing += (s, e) =>
            {
                _pollTimer?.Stop();
                try { KeymapRepository.Save(_store); } catch { }
                _input?.Dispose();
                _host?.Dispose();
                _control?.Dispose();
                _overlay?.Close();
            };
        }

        private void SetControlMode(ControlMode mode)
        {
            _input.ReleaseAllInputs();
            _input.Mode = mode;
            if (_overlay != null) _overlay.ClickThrough = (mode == ControlMode.Touch);

            _btnModeTouch.BackColor = mode == ControlMode.Touch ? Color.FromArgb(255, 105, 0) : Color.FromArgb(14, 19, 27);
            _btnModePlay.BackColor = mode == ControlMode.Play ? Color.FromArgb(255, 105, 0) : Color.FromArgb(14, 19, 27);
            _btnModeEdit.BackColor = mode == ControlMode.Edit ? Color.FromArgb(255, 105, 0) : Color.FromArgb(14, 19, 27);

            _editPanel.Visible = mode == ControlMode.Edit;
            _lblPlayHint.Visible = mode == ControlMode.Play;

            _lblHudMode.Text = mode == ControlMode.Touch ? "模式: 🖱️ 鼠标触控"
                : mode == ControlMode.Play ? "模式: 🎮 键鼠映射激活"
                : "模式: 🛠️ 键位可视化编辑";
            SyncOverlay();
        }

        private void RefreshPresetCombo(string selectKey)
        {
            _updatingPresetCombo = true;
            try
            {
                _cmbPreset.Items.Clear();
                _presetKeys.Clear();
                int selIdx = 0;
                int i = 0;
                foreach (var kv in _store.Presets)
                {
                    _presetKeys.Add(kv.Key);
                    _cmbPreset.Items.Add(kv.Value.Name ?? kv.Key);
                    if (kv.Key == selectKey) selIdx = i;
                    i++;
                }
                if (_cmbPreset.Items.Count > 0)
                {
                    _cmbPreset.SelectedIndex = Math.Max(0, Math.Min(_cmbPreset.Items.Count - 1, selIdx));
                }
            }
            finally
            {
                _updatingPresetCombo = false;
            }
        }

        private void ApplyPresetToUI(string key)
        {
            if (!_store.Presets.ContainsKey(key) && _store.Presets.Count > 0)
            {
                foreach (var k in _store.Presets.Keys) { key = k; break; }
            }
            _store.ActivePresetKey = key;
            RefreshPresetCombo(key);
            if (_store.Presets.ContainsKey(key))
            {
                _input.ActivePreset = _store.Presets[key];
                var joy = _input.ActivePreset.Items.Find(i => i.Type == "joystick");
                if (joy != null)
                {
                    _trkJoyRadius.Value = Math.Max(_trkJoyRadius.Minimum, Math.Min(_trkJoyRadius.Maximum, (int)Math.Round(joy.Radius * 100)));
                }
            }
            _input.MouseSensitivity = _store.MouseSensitivity;
            _trkSens.Value = Math.Max(_trkSens.Minimum, Math.Min(_trkSens.Maximum, (int)Math.Round(_store.MouseSensitivity * 10)));
            _overlay?.RefreshOverlay();
        }

        private async Task RefreshStatusAsync()
        {
            var st = await _adb.PollStatusAsync(!_host.IsRunning);
            if (st.AdbState == "device")
            {
                _lblConnBadge.Text = "已就绪 (Type-C)";
                _lblConnBadge.BackColor = Color.FromArgb(18, 60, 48);
                _lblConnBadge.ForeColor = Color.FromArgb(52, 211, 153);
                _lblModel.Text = st.MarketName ?? "Android";
                _lblOs.Text = $"{st.HyperOsVersion} (Android {st.AndroidVersion})";
                _lblRes.Text = st.Resolution ?? "--";
                _lblBat.Text = st.BatteryLevel.HasValue ? $"{st.BatteryLevel}%" : "--";
                _lblStandbyTitle.Text = $"已连接 {st.MarketName}！";
                _lblStandbyDesc.Text = "设备已授权就绪，点击下方按钮即可开启 120FPS D3D11VA 原生投屏与游戏键鼠映射。";
            }
            else if (st.AdbState == "unauthorized")
            {
                _lblConnBadge.Text = "待手机允许调试";
                _lblConnBadge.BackColor = Color.FromArgb(65, 50, 18);
                _lblConnBadge.ForeColor = Color.FromArgb(251, 191, 36);
                _lblStandbyTitle.Text = "请在手机上点击「允许 USB 调试」";
            }
            else if (st.UsbDetected)
            {
                _lblConnBadge.Text = "已插线·未开调试";
                _lblConnBadge.BackColor = Color.FromArgb(65, 50, 18);
                _lblConnBadge.ForeColor = Color.FromArgb(251, 191, 36);
                _lblStandbyTitle.Text = "已检测到 USB 连接，请开启「USB 调试」";
            }
            else
            {
                _lblConnBadge.Text = "未检测到手机";
                _lblConnBadge.BackColor = Color.FromArgb(72, 24, 28);
                _lblConnBadge.ForeColor = Color.FromArgb(248, 113, 113);
                _lblModel.Text = "未连接";
                _lblOs.Text = "--";
                _lblRes.Text = "--";
                _lblBat.Text = "--";
            }
        }

        private async Task StartMirroringAsync()
        {
            if (_isStarting) return;
            _isStarting = true;
            _btnStart.Enabled = false;
            _btnStart.Text = "⏳ 正在连接设备...";
            try
            {
                await RefreshStatusAsync();
                var st = _adb.CurrentStatus;
                if (st.AdbState != "device" || string.IsNullOrEmpty(st.Serial))
                {
                    MessageBox.Show("未检测到已授权的手机，请确认 Type-C 数据线已插好并开启「USB 调试」与「USB 调试（安全设置）」。", "DroidDeck", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                int[] sizes = { 1280, 1920, 0 };
                int[] fpsArr = { 60, 90, 120 };
                int[] bitArr = { 8, 16, 24 };

                int maxSize = sizes[Math.Max(0, _cmbMaxSize.SelectedIndex)];
                int maxFps = fpsArr[Math.Max(0, _cmbMaxFps.SelectedIndex)];
                int bitM = bitArr[Math.Max(0, _cmbBitRate.SelectedIndex)];

                await _control.StartAsync(st.Serial);
                await _host.StartAsync(st.Serial, maxSize, maxFps, bitM, _chkAudio.Checked, _chkTurnScreenOff.Checked, _chkAntiAlias.Checked);
                if (_chkTurnScreenOff.Checked)
                {
                    _control.SetDisplayPower(false);
                }
            }
            catch (Exception ex)
            {
                StopMirroring();
                MessageBox.Show("启动投屏失败: " + ex.Message, "DroidDeck", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isStarting = false;
                _btnStart.Enabled = true;
                _btnStart.Text = "▶ 启动 120FPS 原生投屏";
            }
        }

        private void StopMirroring()
        {
            _input.ReleaseAllInputs();
            _host.Stop();
            _control.Stop();
        }

        private async Task TakeScreenshotAsync()
        {
            var st = _adb.CurrentStatus;
            if (st.AdbState != "device" || string.IsNullOrEmpty(st.Serial))
            {
                MessageBox.Show("请先连接手机！", "DroidDeck");
                return;
            }
            try
            {
                byte[] png = await Task.Run(() => _adb.RunAdbBinarySync($"-s {st.Serial} exec-out screencap -p", 8000));
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "DroidDeck");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, $"DroidDeck-{DateTime.Now:yyyyMMdd-HHmmss}.png");
                File.WriteAllBytes(file, png);
                Process.Start("explorer.exe", $"/select,\"{file}\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show("截图失败: " + ex.Message, "DroidDeck");
            }
        }

        private async Task ResetAdbAsync()
        {
            await _adb.RestartAdbAsync();
            await RefreshStatusAsync();
        }

        private void AddTapKey() => AddTapKeyAt(0.5, 0.5);

        private void AddTapKeyAt(double nx, double ny)
        {
            var item = new KeymapItem
            {
                Id = "tap_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Type = "tap",
                X = nx,
                Y = ny,
                VirtualKey = 0x47,
                KeyCode = "G",
                KeyName = "G",
                Label = "新按键"
            };
            _input.ActivePreset?.Items.Add(item);
            _overlay.RefreshOverlay();
            using (var dlg = new KeyBindDialog(item, _input, () => _overlay.RefreshOverlay()))
            {
                if (dlg.ShowDialog(this) == DialogResult.Abort)
                {
                    _input.ActivePreset?.Items.Remove(item);
                    _overlay.RefreshOverlay();
                }
            }
            KeymapRepository.Save(_store);
        }

        private void CreateCustomPreset()
        {
            string name;
            bool cloneCurrent;
            if (!SchemePrompt.ShowNewScheme(this, out name, out cloneCurrent)) return;

            string key = "custom_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var newPreset = new KeymapPreset { Key = key, Name = name, Items = new List<KeymapItem>() };
            if (cloneCurrent && _input.ActivePreset != null)
            {
                foreach (var it in _input.ActivePreset.Items)
                {
                    newPreset.Items.Add(new KeymapItem
                    {
                        Id = it.Id,
                        Type = it.Type,
                        X = it.X,
                        Y = it.Y,
                        Radius = it.Radius,
                        KeyCode = it.KeyCode,
                        VirtualKey = it.VirtualKey,
                        KeyName = it.KeyName,
                        Label = it.Label
                    });
                }
            }
            _store.Presets[key] = newPreset;
            ApplyPresetToUI(key);
            KeymapRepository.Save(_store);
            SetControlMode(ControlMode.Edit);
        }

        private void RenameCurrentPreset()
        {
            if (_input.ActivePreset == null) return;
            string newName = SchemePrompt.ShowRename(this, _input.ActivePreset.Name);
            if (string.IsNullOrEmpty(newName)) return;
            _input.ActivePreset.Name = newName;
            RefreshPresetCombo(_store.ActivePresetKey);
            KeymapRepository.Save(_store);
        }

        private void DeleteCurrentPreset()
        {
            if (_store.Presets.Count <= 1)
            {
                MessageBox.Show("至少需要保留一套映射方案。", "DroidDeck");
                return;
            }
            string key = _store.ActivePresetKey;
            string title = _input.ActivePreset?.Name ?? key;
            if (MessageBox.Show($"确定删除映射方案「{title}」吗？", "DroidDeck", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            _store.Presets.Remove(key);
            string nextKey = null;
            foreach (var k in _store.Presets.Keys) { nextKey = k; break; }
            ApplyPresetToUI(nextKey);
            KeymapRepository.Save(_store);
        }

        private void AddJoystick()
        {
            if (_input.ActivePreset?.Items.Exists(i => i.Type == "joystick") == true)
            {
                MessageBox.Show("当前预设已有 WASD 摇杆，请直接拖拽调整位置。", "DroidDeck");
                return;
            }
            _input.ActivePreset?.Items.Add(new KeymapItem { Id = "joy_" + DateTime.Now.Ticks, Type = "joystick", X = 0.17, Y = 0.75, Radius = 0.12, Label = "WASD" });
            _overlay.RefreshOverlay();
        }

        private void AddAimZone()
        {
            var existing = _input.ActivePreset?.Items.Find(i => i.Type == "aim");
            if (existing != null)
            {
                using (var dlg = new KeyBindDialog(existing, _input, () => _overlay.RefreshOverlay()))
                {
                    if (dlg.ShowDialog(this) == DialogResult.Abort)
                    {
                        _input.ActivePreset?.Items.Remove(existing);
                        _overlay.RefreshOverlay();
                    }
                    KeymapRepository.Save(_store);
                }
                return;
            }
            var aimItem = new KeymapItem { Id = "aim_" + DateTime.Now.Ticks, Type = "aim", X = 0.66, Y = 0.48, VirtualKey = 0xC0, KeyCode = "Oemtilde", KeyName = "~", Label = "视角中心" };
            _input.ActivePreset?.Items.Add(aimItem);
            _overlay.RefreshOverlay();
            KeymapRepository.Save(_store);
        }

        private void ResetPreset()
        {
            var def = KeymapRepository.CreateDefaults();
            string key = _store.ActivePresetKey;
            if (def.Presets.ContainsKey(key))
            {
                _store.Presets[key] = def.Presets[key];
                ApplyPresetToUI(key);
                KeymapRepository.Save(_store);
            }
            else if (_input.ActivePreset != null)
            {
                if (MessageBox.Show("确定清空当前自定义方案的所有键位吗？", "DroidDeck", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    _input.ActivePreset.Items.Clear();
                    _overlay.RefreshOverlay();
                    KeymapRepository.Save(_store);
                }
            }
        }

        private void ToggleSidebar()
        {
            _sidebar.Visible = !_sidebar.Visible;
            _btnToggleSidebar.Text = _sidebar.Visible ? "◀ 收起侧栏" : "▶ 展开侧栏";
            BeginInvoke(new Action(() =>
            {
                _host.LayoutEmbeddedWindow();
                SyncOverlay();
            }));
        }

        private void ToggleFullscreen()
        {
            if (!_isFullscreen)
            {
                _prevWindowState = WindowState;
                _prevBounds = Bounds;
                _prevSidebarVisible = _sidebar.Visible;

                _isFullscreen = true;
                _sidebar.Visible = false;
                _btnToggleSidebar.Text = "▶ 展开侧栏";
                _btnFullscreen.Text = "✕ 退出全屏";

                var scr = Screen.FromControl(this);
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Normal;
                Bounds = scr.Bounds;
            }
            else
            {
                _isFullscreen = false;
                FormBorderStyle = FormBorderStyle.Sizable;
                Bounds = _prevBounds;
                WindowState = _prevWindowState;
                _sidebar.Visible = _prevSidebarVisible;
                _btnToggleSidebar.Text = _sidebar.Visible ? "◀ 收起侧栏" : "▶ 展开侧栏";
                _btnFullscreen.Text = "⛶ 全屏 (F11)";
            }

            BeginInvoke(new Action(() =>
            {
                _host.LayoutEmbeddedWindow();
                SyncOverlay();
            }));
        }

        // UI Builder Helpers
        private Label MakeHudPill(string text, int x)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, 8),
                AutoSize = true,
                BackColor = Color.FromArgb(26, 33, 48),
                ForeColor = Color.FromArgb(226, 232, 240),
                Padding = new Padding(8, 4, 8, 4),
                Font = new Font("Microsoft YaHei UI", 9f)
            };
        }

        private GroupBox MakeCard(int x, int y, int w, int h, string title)
        {
            return new GroupBox
            {
                Text = title,
                Location = new Point(x, y),
                Size = new Size(w, h),
                BackColor = Color.FromArgb(26, 33, 48),
                ForeColor = Color.FromArgb(226, 232, 240),
                Font = new Font("Microsoft YaHei UI", 9.2f, FontStyle.Bold)
            };
        }

        private Label AddInfoField(Control parent, string label, string value, int x, int y)
        {
            var l = new Label { Text = label, Location = new Point(x, y), Size = new Size(150, 17), ForeColor = Color.FromArgb(148, 163, 184), Font = new Font("Microsoft YaHei UI", 8.5f) };
            var v = new Label { Text = value, Location = new Point(x, y + 17), Size = new Size(156, 20), ForeColor = Color.White, Font = new Font("Microsoft YaHei UI", 9.2f, FontStyle.Bold) };
            parent.Controls.Add(l);
            parent.Controls.Add(v);
            return v;
        }

        private ComboBox AddCombo(Control parent, string label, int x, int y, int w, string[] items, int defaultIdx)
        {
            var l = new Label { Text = label, Location = new Point(x, y), Size = new Size(w, 17), ForeColor = Color.FromArgb(148, 163, 184), Font = new Font("Microsoft YaHei UI", 8.5f) };
            var c = new ComboBox
            {
                Location = new Point(x, y + 18),
                Size = new Size(w, 24),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(15, 20, 29),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            c.Items.AddRange(items);
            c.SelectedIndex = defaultIdx;
            parent.Controls.Add(l);
            parent.Controls.Add(c);
            return c;
        }

        private Button MakePrimaryButton(string text, int x, int y, int w, int h)
        {
            return new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(255, 105, 0),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
        }

        private Button MakeModeBtn(string text, int x, int y, int w, bool active)
        {
            return new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = active ? Color.FromArgb(255, 105, 0) : Color.FromArgb(14, 19, 27),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
        }

        private void AddQuickBtn(Control parent, string text, int x, int y, int w, Action onClick)
        {
            var b = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(18, 23, 34),
                ForeColor = Color.FromArgb(203, 213, 225),
                Font = new Font("Microsoft YaHei UI", 8f),
                Cursor = Cursors.Hand
            };
            b.Click += (s, e) => onClick();
            parent.Controls.Add(b);
        }
    }
}
