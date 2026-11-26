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
        private readonly NumericUpDown _numX;
        private readonly NumericUpDown _numY;
        private readonly NumericUpDown _numRad;
        private int _pendingVk;
        private string _pendingKeyName;
        private string _pendingKeyCode;
        private readonly Label _lblPreview;
        private bool _updatingCoords;

        public KeyBindDialog(KeymapItem item, InputEngine input, Action onChanged)
        {
            _item = item;
            _input = input;
            _onChanged = onChanged;
            _pendingVk = item.VirtualKey;
            _pendingKeyName = item.KeyName ?? KeymapRepository.FormatVirtualKeyName(item.VirtualKey);
            _pendingKeyCode = item.KeyCode ?? "";

            bool isAim = item.Type == "aim";
            bool isJoy = item.Type == "joystick";
            Text = isAim ? "自定义鼠标锁定中心与热键" : isJoy ? "自定义 WASD 摇杆" : "自定义按键映射";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(410, isAim ? 385 : isJoy ? 245 : 315);
            BackColor = Color.FromArgb(26, 33, 48);
            ForeColor = Color.FromArgb(241, 245, 249);
            Font = new Font("Microsoft YaHei UI", 9f);

            var lblTitle = new Label
            {
                Text = isAim ? "按下任意键修改开关热键，或精确设置锁定中心坐标"
                     : isJoy ? "调整虚拟摇杆显示名称、屏幕中心与半径大小"
                     : "按下要绑定的键盘按键（或点击鼠标按键）",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(12, 10),
                Size = new Size(372, 26),
                Font = new Font("Microsoft YaHei UI", 9.2f, FontStyle.Bold)
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
                Size = new Size(272, 24),
                BackColor = Color.FromArgb(13, 18, 26),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            int y = 78;
            if (!isJoy)
            {
                _lblPreview = new Label
                {
                    Text = $"当前绑定：{(_pendingKeyName ?? "?")}",
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Location = new Point(24, y),
                    Size = new Size(346, 42),
                    BackColor = Color.FromArgb(13, 18, 26),
                    ForeColor = Color.FromArgb(255, 176, 116),
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = new Font("Segoe UI", 11.5f, FontStyle.Bold)
                };

                var btnLmb = MakeBtn("左键", 24, y + 50, 64);
                var btnRmb = MakeBtn("右键", 94, y + 50, 64);
                var btnMmb = MakeBtn("中键", 164, y + 50, 64);
                var btnX1 = MakeBtn("侧键4", 234, y + 50, 65);
                var btnX2 = MakeBtn("侧键5", 305, y + 50, 65);

                btnLmb.Click += (s, e) => SetPendingBind(-1, "左键", "MouseLeft");
                btnRmb.Click += (s, e) => SetPendingBind(-2, "右键", "MouseRight");
                btnMmb.Click += (s, e) => SetPendingBind(-3, "中键", "MouseMiddle");
                btnX1.Click += (s, e) => SetPendingBind(-4, "侧键4", "MouseX1");
                btnX2.Click += (s, e) => SetPendingBind(-5, "侧键5", "MouseX2");

                Controls.Add(_lblPreview);
                Controls.Add(btnLmb);
                Controls.Add(btnRmb);
                Controls.Add(btnMmb);
                Controls.Add(btnX1);
                Controls.Add(btnX2);
                y += 96;
            }

            // Custom X/Y coordinates (plus Radius for joystick)
            var lblX = new Label { Text = "横坐标 X(%):", Location = new Point(24, y + 4), Size = new Size(80, 20), ForeColor = Color.FromArgb(148, 163, 184) };
            _numX = MakeNum(106, y, (decimal)Math.Round(item.X * 100), 4, 96);
            var lblY = new Label { Text = "纵坐标 Y(%):", Location = new Point(204, y + 4), Size = new Size(80, 20), ForeColor = Color.FromArgb(148, 163, 184) };
            _numY = MakeNum(286, y, (decimal)Math.Round(item.Y * 100), 4, 96);

            _numX.ValueChanged += (s, e) => ApplyCoordsLive();
            _numY.ValueChanged += (s, e) => ApplyCoordsLive();
            Controls.Add(lblX);
            Controls.Add(_numX);
            Controls.Add(lblY);
            Controls.Add(_numY);
            y += 36;

            if (isJoy)
            {
                var lblR = new Label { Text = "摇杆半径(%):", Location = new Point(24, y + 4), Size = new Size(80, 20), ForeColor = Color.FromArgb(148, 163, 184) };
                _numRad = MakeNum(106, y, (decimal)Math.Round(item.Radius * 100), 6, 22);
                _numRad.ValueChanged += (s, e) => ApplyCoordsLive();
                Controls.Add(lblR);
                Controls.Add(_numRad);
                y += 36;
            }

            if (isAim)
            {
                var btnCenter = MakeBtn("🎯 画面正中 (50%,50%)", 24, y, 169);
                var btnRightLook = MakeBtn("🎯 右半区视角 (68%,48%)", 201, y, 169);
                btnCenter.Click += (s, e) => SetCoords(0.50, 0.50);
                btnRightLook.Click += (s, e) => SetCoords(0.68, 0.48);
                Controls.Add(btnCenter);
                Controls.Add(btnRightLook);
                y += 42;
            }

            var btnSave = MakeBtn("✓ 确认保存", 24, y + 4, 222);
            btnSave.BackColor = Color.FromArgb(255, 105, 0);
            btnSave.ForeColor = Color.White;
            btnSave.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            btnSave.Click += (s, e) => CommitAndClose();

            var btnDel = MakeBtn("🗑 删除此键位", 254, y + 4, 116);
            btnDel.ForeColor = Color.FromArgb(248, 113, 113);
            btnDel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Abort;
                Close();
            };

            Controls.Add(lblTitle);
            Controls.Add(lblNamePrompt);
            Controls.Add(_txtLabel);
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

        private void SetCoords(double nx, double ny)
        {
            _updatingCoords = true;
            _numX.Value = (decimal)Math.Round(nx * 100);
            _numY.Value = (decimal)Math.Round(ny * 100);
            _updatingCoords = false;
            ApplyCoordsLive();
        }

        private void ApplyCoordsLive()
        {
            if (_updatingCoords) return;
            _item.X = (double)_numX.Value / 100.0;
            _item.Y = (double)_numY.Value / 100.0;
            if (_numRad != null) _item.Radius = (double)_numRad.Value / 100.0;
            _onChanged?.Invoke();
        }

        private void OnKeyCaptured(int vk)
        {
            if (_item.Type == "joystick" || _txtLabel.Focused || _numX.ContainsFocus || _numY.ContainsFocus) return;
            SetPendingBind(vk, KeymapRepository.FormatVirtualKeyName(vk), ((Keys)Math.Max(0, vk)).ToString());
        }

        private void SetPendingBind(int vk, string keyName, string keyCode)
        {
            _pendingVk = vk;
            _pendingKeyName = keyName;
            _pendingKeyCode = keyCode;
            if (_lblPreview != null) _lblPreview.Text = $"已捕获按键：{keyName}";
        }

        private void CommitAndClose()
        {
            _item.VirtualKey = _pendingVk;
            _item.KeyName = _pendingKeyName;
            _item.KeyCode = _pendingKeyCode;
            _item.Label = _txtLabel.Text.Trim();
            ApplyCoordsLive();
            DialogResult = DialogResult.OK;
            Close();
        }

        private NumericUpDown MakeNum(int x, int y, decimal val, decimal min, decimal max)
        {
            return new NumericUpDown
            {
                Location = new Point(x, y),
                Size = new Size(76, 24),
                Minimum = min,
                Maximum = max,
                Value = Math.Max(min, Math.Min(max, val)),
                BackColor = Color.FromArgb(13, 18, 26),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
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

    public static class SchemePrompt
    {
        public static bool ShowNewScheme(Form owner, out string name, out bool cloneCurrent)
        {
            name = null;
            cloneCurrent = true;
            using (var dlg = new Form
            {
                Text = "新建自定义键鼠方案",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(360, 195),
                BackColor = Color.FromArgb(26, 33, 48),
                ForeColor = Color.FromArgb(241, 245, 249),
                Font = new Font("Microsoft YaHei UI", 9f)
            })
            {
                var lbl = new Label { Text = "方案名称（如：三角洲行动 / 永劫无间）：", Location = new Point(20, 16), Size = new Size(310, 20), ForeColor = Color.FromArgb(203, 213, 225) };
                var txt = new TextBox { Text = "我的自定义方案", Location = new Point(20, 42), Size = new Size(305, 26), BackColor = Color.FromArgb(13, 18, 26), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
                var chkClone = new CheckBox { Text = "复制当前方案的键位作为起点（不勾选则创建空白方案）", Checked = true, Location = new Point(20, 76), Size = new Size(315, 24), ForeColor = Color.FromArgb(148, 163, 184) };
                var btnOk = new Button { Text = "✓ 创建并编辑", Location = new Point(20, 112), Size = new Size(195, 34), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(255, 105, 0), ForeColor = Color.White, Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold), DialogResult = DialogResult.OK };
                var btnCancel = new Button { Text = "取消", Location = new Point(225, 112), Size = new Size(100, 34), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(18, 23, 34), ForeColor = Color.Gainsboro, DialogResult = DialogResult.Cancel };
                dlg.Controls.AddRange(new Control[] { lbl, txt, chkClone, btnOk, btnCancel });
                dlg.AcceptButton = btnOk;
                dlg.CancelButton = btnCancel;
                if (dlg.ShowDialog(owner) == DialogResult.OK)
                {
                    string v = txt.Text.Trim();
                    if (!string.IsNullOrEmpty(v))
                    {
                        name = v;
                        cloneCurrent = chkClone.Checked;
                        return true;
                    }
                }
                return false;
            }
        }

        public static string ShowRename(Form owner, string currentName)
        {
            using (var dlg = new Form
            {
                Text = "重命名方案",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(350, 160),
                BackColor = Color.FromArgb(26, 33, 48),
                ForeColor = Color.FromArgb(241, 245, 249),
                Font = new Font("Microsoft YaHei UI", 9f)
            })
            {
                var lbl = new Label { Text = "输入新的方案名称：", Location = new Point(20, 16), Size = new Size(300, 20), ForeColor = Color.FromArgb(203, 213, 225) };
                var txt = new TextBox { Text = currentName ?? "", Location = new Point(20, 42), Size = new Size(295, 26), BackColor = Color.FromArgb(13, 18, 26), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
                var btnOk = new Button { Text = "✓ 确定", Location = new Point(20, 78), Size = new Size(185, 34), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(255, 105, 0), ForeColor = Color.White, Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold), DialogResult = DialogResult.OK };
                var btnCancel = new Button { Text = "取消", Location = new Point(215, 78), Size = new Size(100, 34), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(18, 23, 34), ForeColor = Color.Gainsboro, DialogResult = DialogResult.Cancel };
                dlg.Controls.AddRange(new Control[] { lbl, txt, btnOk, btnCancel });
                dlg.AcceptButton = btnOk;
                dlg.CancelButton = btnCancel;
                if (dlg.ShowDialog(owner) == DialogResult.OK)
                {
                    string v = txt.Text.Trim();
                    return string.IsNullOrEmpty(v) ? null : v;
                }
                return null;
            }
        }
    }
}
