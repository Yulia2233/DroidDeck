using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using DroidDeck.Core;
using DroidDeck.Models;

namespace DroidDeck.UI
{
    /// <summary>
    /// Per-pixel alpha layered overlay covering the embedded scrcpy video rect.
    /// In Touch mode (ClickThrough=true), uses WS_EX_TRANSPARENT + Alpha=0 so native scrcpy.exe
    /// handles mouse clicks, swipes, and wheel events with 1:1 accuracy.
    /// In Play/Edit modes (ClickThrough=false), uses Alpha=1 background to capture mouse events cleanly.
    /// </summary>
    public class OverlayForm : Form
    {
        private readonly InputEngine _input;
        private bool _showOverlay = true;
        private bool _clickThrough = true;
        private Bitmap _layerBmp;
        private Graphics _layerGraphics;
        private readonly Font _fontKey = new Font("Segoe UI", 10f, FontStyle.Bold);
        private readonly Font _fontLabel = new Font("Microsoft YaHei UI", 8f, FontStyle.Regular);
        private readonly StringFormat _sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        public bool ShowOverlay
        {
            get => _showOverlay;
            set
            {
                _showOverlay = value;
                if (_clickThrough && !_showOverlay && Visible) Hide();
                else RenderLayered();
            }
        }

        public bool ClickThrough
        {
            get => _clickThrough;
            set
            {
                if (_clickThrough == value) return;
                _clickThrough = value;
                ApplyHitTestStyle();
                RenderLayered();
            }
        }

        public OverlayForm(Form owner, InputEngine input)
        {
            _input = input;
            Owner = owner;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;

            MouseDown += (s, e) => _input.HandleSurfaceMouseDown(e.Button, PointToScreen(e.Location));
            MouseMove += (s, e) => _input.HandleSurfaceMouseMove(PointToScreen(e.Location));
            MouseUp += (s, e) => _input.HandleSurfaceMouseUp(e.Button, PointToScreen(e.Location));
            MouseDoubleClick += (s, e) => _input.HandleSurfaceDoubleClick(PointToScreen(e.Location));
            MouseWheel += (s, e) => _input.HandleSurfaceMouseWheel(e.Delta, PointToScreen(e.Location));
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
                if (_clickThrough) cp.ExStyle |= NativeMethods.WS_EX_TRANSPARENT;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;

        private void ApplyHitTestStyle()
        {
            if (!IsHandleCreated) return;
            long ex = NativeMethods.GetWindowLongPtr(Handle, NativeMethods.GWL_EXSTYLE).ToInt64();
            if (_clickThrough)
                ex |= NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_NOACTIVATE;
            else
                ex &= ~(long)NativeMethods.WS_EX_TRANSPARENT;
            NativeMethods.SetWindowLongPtr(Handle, NativeMethods.GWL_EXSTYLE, new IntPtr(ex));
        }

        public void SyncBounds(Rectangle screenRect)
        {
            bool shouldHide = Owner == null
                || Owner.WindowState == FormWindowState.Minimized
                || screenRect.Width <= 10
                || screenRect.Height <= 10
                || (_clickThrough && !_showOverlay);

            if (shouldHide)
            {
                if (Visible) Hide();
                return;
            }

            Bounds = screenRect;
            if (!Visible)
            {
                Show(Owner);
                ApplyHitTestStyle();
            }
            RenderLayered();
        }

        public void RefreshOverlay()
        {
            RenderLayered();
        }

        public void RenderLayered()
        {
            if (!IsHandleCreated || IsDisposed || !Visible || Width <= 0 || Height <= 0) return;

            if (_layerBmp == null || _layerBmp.Width != Width || _layerBmp.Height != Height)
            {
                _layerGraphics?.Dispose();
                _layerBmp?.Dispose();
                _layerBmp = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
                _layerGraphics = Graphics.FromImage(_layerBmp);
                _layerGraphics.CompositingQuality = CompositingQuality.HighSpeed;
                _layerGraphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                _layerGraphics.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                _layerGraphics.SmoothingMode = SmoothingMode.AntiAlias;
                _layerGraphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            }

            _layerGraphics.CompositingMode = CompositingMode.SourceCopy;
            _layerGraphics.Clear(_clickThrough ? Color.FromArgb(0, 0, 0, 0) : Color.FromArgb(1, 0, 0, 0));
            _layerGraphics.CompositingMode = CompositingMode.SourceOver;

            if (_showOverlay || _input.Mode == ControlMode.Edit)
            {
                DrawOverlayItems(_layerGraphics);
            }

            IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
            IntPtr memDc = NativeMethods.CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;

            try
            {
                hBitmap = _layerBmp.GetHbitmap(Color.FromArgb(0));
                oldBitmap = NativeMethods.SelectObject(memDc, hBitmap);

                var topPos = new NativeMethods.POINT { X = Left, Y = Top };
                var size = new NativeMethods.SIZE { cx = Width, cy = Height };
                var srcPt = new NativeMethods.POINT { X = 0, Y = 0 };
                var blend = new NativeMethods.BLENDFUNCTION
                {
                    BlendOp = NativeMethods.AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = NativeMethods.AC_SRC_ALPHA
                };

                NativeMethods.UpdateLayeredWindow(Handle, screenDc, ref topPos, ref size, memDc, ref srcPt, 0, ref blend, NativeMethods.ULW_ALPHA);
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero) NativeMethods.SelectObject(memDc, oldBitmap);
                if (hBitmap != IntPtr.Zero) NativeMethods.DeleteObject(hBitmap);
                NativeMethods.DeleteDC(memDc);
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private void DrawOverlayItems(Graphics g)
        {
            if (_input.Mode == ControlMode.Edit)
            {
                using (var borderPen = new Pen(Color.FromArgb(200, 59, 130, 246), 2f) { DashStyle = DashStyle.Dash })
                {
                    g.DrawRectangle(borderPen, 1, 1, Width - 3, Height - 3);
                }
            }

            var preset = _input.ActivePreset;
            if (preset == null) return;

            int minSide = Math.Min(Width, Height);
            bool suspendMouse = _input.ShouldSuspendMouseMappings;

            foreach (var item in preset.Items)
            {
                float cx = (float)(item.X * Width);
                float cy = (float)(item.Y * Height);

                if (item.Type == "joystick")
                {
                    float r = Math.Max(36f, (float)(item.Radius * minSide));
                    using (var fill = new SolidBrush(Color.FromArgb(140, 15, 23, 42)))
                    using (var pen = new Pen(Color.FromArgb(220, 59, 130, 246), 2f) { DashStyle = DashStyle.Dash })
                    {
                        g.FillEllipse(fill, cx - r, cy - r, r * 2, r * 2);
                        g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
                    }

                    float sx = cx + _input.JoystickOffset.X * Math.Min(26f, r * 0.45f);
                    float sy = cy + _input.JoystickOffset.Y * Math.Min(26f, r * 0.45f);
                    float sr = 19f;
                    using (var sFill = new SolidBrush(Color.FromArgb(210, 59, 130, 246)))
                    using (var sPen = new Pen(Color.FromArgb(240, 191, 219, 254), 2f))
                    {
                        g.FillEllipse(sFill, sx - sr, sy - sr, sr * 2, sr * 2);
                        g.DrawEllipse(sPen, sx - sr, sy - sr, sr * 2, sr * 2);
                        g.DrawString("WASD", _fontLabel, Brushes.White, new RectangleF(sx - sr, sy - sr, sr * 2, sr * 2), _sfCenter);
                    }
                }
                else if (item.Type == "aim")
                {
                    if (_input.Mode == ControlMode.Edit)
                    {
                        float halfW = Math.Max(40f, Width * 0.22f);
                        float halfH = Math.Max(40f, Height * 0.26f);
                        float bx1 = Math.Max(Width * 0.05f, cx - halfW);
                        float bx2 = Math.Min(Width * 0.95f, cx + halfW);
                        float by1 = Math.Max(Height * 0.06f, cy - halfH);
                        float by2 = Math.Min(Height * 0.94f, cy + halfH);
                        using (var zonePen = new Pen(Color.FromArgb(140, 16, 185, 129), 1.5f) { DashStyle = DashStyle.Dot })
                        {
                            g.DrawRectangle(zonePen, bx1, by1, Math.Max(10, bx2 - bx1), Math.Max(10, by2 - by1));
                        }
                    }

                    float r = 30f;
                    bool locked = _input.IsAimLocked;
                    Color fillC = locked ? Color.FromArgb(200, 16, 185, 129) : Color.FromArgb(150, 15, 23, 42);
                    Color penC = Color.FromArgb(230, 16, 185, 129);
                    using (var fill = new SolidBrush(fillC))
                    using (var pen = new Pen(penC, 2.2f))
                    using (var crossPen = new Pen(Color.FromArgb(190, 110, 231, 183), 1.4f))
                    {
                        g.FillEllipse(fill, cx - r, cy - r, r * 2, r * 2);
                        g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
                        g.DrawLine(crossPen, cx - r - 8, cy, cx - r + 4, cy);
                        g.DrawLine(crossPen, cx + r - 4, cy, cx + r + 8, cy);
                        g.DrawLine(crossPen, cx, cy - r - 8, cx, cy - r + 4);
                        g.DrawLine(crossPen, cx, cy + r - 4, cx, cy + r + 8);
                        g.DrawString("🎯 " + (item.KeyName ?? "~"), _fontKey, Brushes.White, new RectangleF(cx - r, cy - 14, r * 2, 20), _sfCenter);
                        g.DrawString(locked ? "已锁定" : (item.Label ?? "视角中心"), _fontLabel, Brushes.LightGreen, new RectangleF(cx - r, cy + 4, r * 2, 16), _sfCenter);
                    }
                }
                else
                {
                    bool pressed = _input.PressedItemIds.Contains(item.Id);
                    bool suspended = item.VirtualKey < 0 && suspendMouse;
                    float r = 21f;
                    Color fillC = suspended ? Color.FromArgb(75, 15, 23, 42)
                        : pressed ? Color.FromArgb(235, 255, 105, 0) : Color.FromArgb(185, 15, 23, 42);
                    Color borderC = suspended ? Color.FromArgb(110, 148, 163, 184) : Color.FromArgb(235, 255, 105, 0);
                    Brush textBrush = suspended ? Brushes.Silver : Brushes.White;
                    using (var fill = new SolidBrush(fillC))
                    using (var pen = new Pen(borderC, suspended ? 1.5f : 2f))
                    {
                        if (suspended) pen.DashStyle = DashStyle.Dash;
                        g.FillEllipse(fill, cx - r, cy - r, r * 2, r * 2);
                        g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
                        g.DrawString(item.KeyName ?? "?", _fontKey, textBrush, new RectangleF(cx - r, cy - r, r * 2, r * 2), _sfCenter);
                    }

                    if (!string.IsNullOrEmpty(item.Label))
                    {
                        using (var bg = new SolidBrush(Color.FromArgb(180, 11, 14, 20)))
                        {
                            var lblRect = new RectangleF(cx - 32, cy + r + 2, 64, 15);
                            g.FillRectangle(bg, lblRect);
                            g.DrawString(item.Label, _fontLabel, Brushes.Gainsboro, lblRect, _sfCenter);
                        }
                    }
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _layerGraphics?.Dispose();
                _layerBmp?.Dispose();
                _fontKey.Dispose();
                _fontLabel.Dispose();
                _sfCenter.Dispose();
                _layerGraphics = null;
                _layerBmp = null;
            }
            base.Dispose(disposing);
        }
    }
}
