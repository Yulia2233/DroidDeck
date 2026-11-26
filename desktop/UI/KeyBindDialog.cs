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

        public KeyBindDialog(KeymapItem item, InputEngine input, Action onChanged)
        {
            _item = item;
            _input = input;
            _onChanged = onChanged;

            Text = "绑定按键";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(370, 225);
            BackColor = Color.FromArgb(26, 33, 48);
            ForeColor = Color.FromArgb(241, 245, 249);
            Font = new Font("Microsoft YaHei UI", 9.5f);

            var lblTitle = new Label
            {
                Text = "请直接按下要绑定的键盘按键",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 36,
                Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold)
            };

            var lblTarget = new Label
            {
                Text = $"当前键位: {item.Label ?? item.Id} ({item.KeyName ?? "?"})",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top,
                Height = 26,
                ForeColor = Color.FromArgb(148, 163, 184)
            };

            var lblPreview = new Label
            {
                Text = "等待按键输入...",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(24, 72),
                Size = new Size(306, 48),
                BackColor = Color.FromArgb(13, 18, 26),
                ForeColor = Color.FromArgb(255, 176, 116),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold)
            };

            var btnLmb = MakeBtn("鼠标左键 (LMB)", 24, 134, 102);
            var btnRmb = MakeBtn("鼠标右键 (RMB)", 132, 134, 102);
            var btnDel = MakeBtn("🗑 删除", 240, 134, 90);
            btnDel.ForeColor = Color.FromArgb(248, 113, 113);

            btnLmb.Click += (s, e) => ApplyBind(-1, "左键");
            btnRmb.Click += (s, e) => ApplyBind(-2, "右键");
            btnDel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Abort;
                Close();
            };

            Controls.Add(lblPreview);
            Controls.Add(btnLmb);
            Controls.Add(btnRmb);
            Controls.Add(btnDel);
            Controls.Add(lblTarget);
            Controls.Add(lblTitle);

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
            ApplyBind(vk, KeymapRepository.FormatVirtualKeyName(vk));
        }

        private void ApplyBind(int vk, string keyName)
        {
            _item.VirtualKey = vk;
            _item.KeyName = keyName;
            _item.KeyCode = ((Keys)Math.Max(0, vk)).ToString();
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
