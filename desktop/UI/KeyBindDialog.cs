using System;
using System.Drawing;
using System.Windows.Forms;
using DroidDeck.Core;
using DroidDeck.Models;

namespace DroidDeck.UI
{
    public class KeyBindDialog : Form
    {
        private readonly KeymapItem _item;
        private readonly InputEngine _input;
        private readonly Action _onChanged;
        private readonly TextBox _txtLabel;
        private readonly Label _lblPos;
        private int _pendingVk;
        private string _pendingKeyName;
        private string _pendingKeyCode;
        private readonly Label _lblPreview;

        public KeyBindDialog(KeymapItem item, InputEngine input, Action onChanged)
        {
            _item = item;
            _input = input;
            _onChanged = onChanged;
            _pendingVk = item.VirtualKey;
            _pendingKeyName = item.KeyName ?? KeymapRepository.FormatVirtualKeyName(item.VirtualKey);
            _pendingKeyCode = item.KeyCode ?? "";

            bool isAim = item.Type == "aim";
            Text = isAim ? "设置鼠标锁定中心与热键" : "设置按键映射";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(400, isAim ? 345 : 275);
            BackColor = Color.FromArgb(26, 33, 48);
            ForeColor = Color.FromArgb(241, 245, 249);
            Font = new Font("Microsoft YaHei UI", 9f);

            var lblTitle = new Label
            {
                Text = isAim ? "按下任意键修改锁定开关热键，或调整锁定中心" : "请直接按下要绑定的键盘按键（或点击下方鼠标键）",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(12, 10),
                Size = new Size(362, 26),
                Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold)
            };

            var lblNamePrompt = new Label
            {
                Text = "显示名称：",
                Location = new Point(24, 44),
                Size = new Size(72, 24),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(148, 163, 184)
            };
            _txtLabel = new TextBox
            {
                Text = item.Label ?? "",
                Location = new Point(98, 44),
                Size = new Size(262, 24),
                BackColor = Color.FromArgb(13, 18, 26),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            _lblPreview = new Label
            {
                Text = $"当前绑定：{(_pendingKeyName ?? "?")}",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(24, 78),
                Size = new Size(336, 44),
                BackColor = Color.FromArgb(13, 18, 26),
                ForeColor = Color.FromArgb(255, 176, 116),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 11.5f, FontStyle.Bold)
            };

            var btnLmb = MakeBtn("左键", 24, 132, 62);
            var btnRmb = MakeBtn("右键", 92, 132, 62);
            var btnMmb = MakeBtn("中键", 160, 132, 62);
            var btnX1 = MakeBtn("侧键4", 228, 132, 63);
            var btnX2 = MakeBtn("侧键5", 297, 132, 63);

            btnLmb.Click += (s, e) => SetPendingBind(-1, "左键", "MouseLeft");
            btnRmb.Click += (s, e) => SetPendingBind(-2, "右键", "MouseRight");
            btnMmb.Click += (s, e) => SetPendingBind(-3, "中键", "MouseMiddle");
            btnX1.Click += (s, e) => SetPendingBind(-4, "侧键4", "MouseX1");
            btnX2.Click += (s, e) => SetPendingBind(-5, "侧键5", "MouseX2");

            int nextY = 178;
            if (isAim)
            {
                _lblPos = new Label
                {
                    Text = $"锁定中心位置：X={Math.Round(item.X * 100)}%, Y={Math.Round(item.Y * 100)}%（也可在编辑模式直接拖拽）",
                    Location = new Point(24, nextY),
                    Size = new Size(336, 20),
                    ForeColor = Color.FromArgb(110, 231, 183),
                    Font = new Font("Microsoft YaHei UI", 8.2f)
                };
                var btnCenter = MakeBtn("🎯 画面正中 (50%,50%)", 24, nextY + 24, 164);
                var btnRightLook = MakeBtn("🎯 右半区视角 (68%,48%)", 196, nextY + 24, 164);
                btnCenter.Click += (s, e) =>
                {
                    _item.X = 0.50;
                    _item.Y = 0.50;
                    _lblPos.Text = "锁定中心位置：X=50%, Y=50%（画面正中）";
                    _onChanged?.Invoke();
                };
                btnRightLook.Click += (s, e) =>
                {
                    _item.X = 0.68;
                    _item.Y = 0.48;
                    _lblPos.Text = "锁定中心位置：X=68%, Y=48%（右半区视角）";
                    _onChanged?.Invoke();
                };
                Controls.Add(_lblPos);
                Controls.Add(btnCenter);
                Controls.Add(btnRightLook);
                nextY += 68;
            }

            var btnSave = MakeBtn("✓ 确认保存", 24, nextY, 212);
            btnSave.BackColor = Color.FromArgb(255, 105, 0);
            btnSave.ForeColor = Color.White;
            btnSave.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            btnSave.Click += (s, e) => CommitAndClose();

            var btnDel = MakeBtn("🗑 删除此键位", 244, nextY, 116);
            btnDel.ForeColor = Color.FromArgb(248, 113, 113);
            btnDel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Abort;
                Close();
            };

            Controls.Add(lblTitle);
            Controls.Add(lblNamePrompt);
            Controls.Add(_txtLabel);
            Controls.Add(_lblPreview);
            Controls.Add(btnLmb);
            Controls.Add(btnRmb);
            Controls.Add(btnMmb);
            Controls.Add(btnX1);
            Controls.Add(btnX2);
            Controls.Add(btnSave);
            Controls.Add(btnDel);

            _input.IsBindingKey = true;
            _input.KeyCapturedForBind += OnKeyCaptured;
            FormClosed += (s, e) =>
            {
                _input.IsBindingKey = false;
                _input.KeyCapturedForBind -= OnKeyCaptured;
            };
        }

        private void OnKeyCaptured(int vk)
        {
            if (_txtLabel.Focused) return;
            SetPendingBind(vk, KeymapRepository.FormatVirtualKeyName(vk), ((Keys)Math.Max(0, vk)).ToString());
        }

        private void SetPendingBind(int vk, string keyName, string keyCode)
        {
            _pendingVk = vk;
            _pendingKeyName = keyName;
            _pendingKeyCode = keyCode;
            _lblPreview.Text = $"已捕获按键：{keyName}";
        }

        private void CommitAndClose()
        {
            _item.VirtualKey = _pendingVk;
            _item.KeyName = _pendingKeyName;
            _item.KeyCode = _pendingKeyCode;
            _item.Label = _txtLabel.Text.Trim();
            _onChanged?.Invoke();
            DialogResult = DialogResult.OK;
            Close();
        }

        private Button MakeBtn(string text, int x, int y, int w)
        {
            return new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(18, 23, 34),
                ForeColor = Color.FromArgb(226, 232, 240),
                Cursor = Cursors.Hand
            };
        }
    }
}
