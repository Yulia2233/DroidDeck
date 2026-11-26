using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DroidDeck.Models;

namespace DroidDeck.Core
{
    public class InputEngine : IDisposable
    {
        private const int POINTER_TOUCH = 1;
        private const int POINTER_JOYSTICK = 10;
        private const int POINTER_AIM = 11;
        private const int POINTER_TAP_BASE = 20;

        private readonly Form _mainForm;
        private readonly ScrcpyHost _host;
        private readonly ScrcpyControlChannel _control;
        private Func<Rectangle> _getVideoScreenRect;
        private Func<Form> _getOverlayForm;

        private IntPtr _kbHook = IntPtr.Zero;
        private IntPtr _mouseHook = IntPtr.Zero;
        private NativeMethods.HookProc _kbProc;
        private NativeMethods.HookProc _mouseProc;

        public ControlMode Mode { get; set; } = ControlMode.Touch;
        public KeymapPreset ActivePreset { get; set; }
        public double MouseSensitivity { get; set; } = 1.2;
        public bool IsBindingKey { get; set; }
        public bool IsAimLocked { get; private set; }
        public HashSet<string> PressedItemIds { get; } = new HashSet<string>();
        public PointF JoystickOffset { get; private set; } = PointF.Empty;

        private bool _touchDragging;
        private int _lastMoveTick;
        private Point _lastMoveDevPt;
        private readonly bool[] _wasd = new bool[4]; // W, A, S, D
        private bool _joystickActive;
        private readonly Dictionary<int, ActiveTap> _activeTaps = new Dictionary<int, ActiveTap>();
        private int _aimCurX;
        private int _aimCurY;
        private bool _cursorHidden;

        // Edit mode drag state
        private KeymapItem _dragItem;
        private Point _dragStartScreen;
        private bool _dragMoved;

        public event Action OverlayVisualChanged;
        public event Action<KeymapItem> EditItemClicked;
        public event Action<KeymapItem> EditItemDeleted;
        public event Action<PointF> EditEmptyDoubleClicked;
        public event Action<int> KeyCapturedForBind;
        public event Action<bool> AimLockChanged;

        private class ActiveTap
        {
            public int PointerId;
            public int X;
            public int Y;
            public string ItemId;
        }

        public InputEngine(Form mainForm, ScrcpyHost host, ScrcpyControlChannel control, Func<Rectangle> getVideoScreenRect, Func<Form> getOverlayForm)
        {
            _mainForm = mainForm;
            _host = host;
            _control = control;
            _getVideoScreenRect = getVideoScreenRect;
            _getOverlayForm = getOverlayForm;

            RegisterRawMouse(mainForm.Handle);
            InstallHooks();
        }

        private void RegisterRawMouse(IntPtr hwnd)
        {
            var rid = new NativeMethods.RAWINPUTDEVICE[1];
            rid[0].usUsagePage = 0x01; // Generic Desktop Controls
            rid[0].usUsage = 0x02;     // Mouse
            rid[0].dwFlags = NativeMethods.RIDEV_INPUTSINK;
            rid[0].hwndTarget = hwnd;
            NativeMethods.RegisterRawInputDevices(rid, 1, (uint)Marshal.SizeOf(typeof(NativeMethods.RAWINPUTDEVICE)));
        }

        private void InstallHooks()
        {
            _kbProc = KeyboardHookCallback;
            _mouseProc = MouseHookCallback;
            IntPtr hMod = NativeMethods.GetModuleHandle(null);
            _kbHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _kbProc, hMod, 0);
            _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, hMod, 0);
        }

        private bool IsAppActiveForeground()
        {
            if (_mainForm.IsDisposed || _mainForm.WindowState == FormWindowState.Minimized) return false;
            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            if (fg == _mainForm.Handle) return true;
            if (_host.EmbeddedHwnd != IntPtr.Zero && fg == _host.EmbeddedHwnd) return true;
            var ov = _getOverlayForm?.Invoke();
            if (ov != null && fg == ov.Handle) return true;
            uint fgPid;
            NativeMethods.GetWindowThreadProcessId(fg, out fgPid);
            if (fgPid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id) return true;
            int scPid = _host.ScrcpyProcessId;
            return scPid > 0 && fgPid == (uint)scPid;
        }

        public void ProcessRawInputMessage(ref Message m)
        {
            if (m.Msg != NativeMethods.WM_INPUT) return;
            if (Mode != ControlMode.Play || !IsAimLocked || !IsAppActiveForeground()) return;

            uint dwSize = 0;
            NativeMethods.GetRawInputData(m.LParam, NativeMethods.RID_INPUT, IntPtr.Zero, ref dwSize, (uint)Marshal.SizeOf(typeof(NativeMethods.RAWINPUTHEADER)));
            if (dwSize == 0) return;

            IntPtr buffer = Marshal.AllocHGlobal((int)dwSize);
            try
            {
                if (NativeMethods.GetRawInputData(m.LParam, NativeMethods.RID_INPUT, buffer, ref dwSize, (uint)Marshal.SizeOf(typeof(NativeMethods.RAWINPUTHEADER))) == dwSize)
                {
                    var raw = Marshal.PtrToStructure<NativeMethods.RAWINPUT>(buffer);
                    if (raw.header.dwType == NativeMethods.RIM_TYPEMOUSE)
                    {
                        int dx = raw.mouse.lLastX;
                        int dy = raw.mouse.lLastY;
                        // Filter out absolute-pointer jumps (MOUSE_MOVE_ABSOLUTE = 0x01) or hardware glitches
                        if ((raw.mouse.usFlags & 0x01) == 0 && (dx != 0 || dy != 0) && Math.Abs(dx) < 250 && Math.Abs(dy) < 250)
                        {
                            HandleAimDelta(dx, dy);
                        }
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && IsAppActiveForeground())
            {
                var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                int vk = (int)info.vkCode;
                bool isDown = (wParam == (IntPtr)NativeMethods.WM_KEYDOWN || wParam == (IntPtr)NativeMethods.WM_SYSKEYDOWN);
                bool isUp = (wParam == (IntPtr)NativeMethods.WM_KEYUP || wParam == (IntPtr)NativeMethods.WM_SYSKEYUP);

                if (IsBindingKey)
                {
                    // Allow typing label text inside KeyBindDialog when focused on the TextBox,
                    // otherwise capture keyboard key immediately (except Esc cancels dialog)
                    if (isDown && !(Form.ActiveForm?.ActiveControl is TextBox))
                    {
                        _mainForm.BeginInvoke(new Action(() => KeyCapturedForBind?.Invoke(vk)));
                        return (IntPtr)1;
                    }
                    return NativeMethods.CallNextHookEx(_kbHook, nCode, wParam, lParam);
                }

                if (Mode == ControlMode.Play)
                {
                    // Esc releases mouse lock immediately
                    if (vk == 0x1B && isDown && IsAimLocked)
                    {
                        _mainForm.BeginInvoke(new Action(() => SetAimLock(false)));
                        return (IntPtr)1;
                    }

                    // Check if key is mapped (WASD, aim toggle, or tap skill)
                    bool handled = false;
                    if (isDown) handled = HandlePlayKeyDown(vk);
                    else if (isUp) handled = HandlePlayKeyUp(vk);

                    if (handled) return (IntPtr)1;
                }
            }
            return NativeMethods.CallNextHookEx(_kbHook, nCode, wParam, lParam);
        }

        public void HandleSurfaceMouseDown(MouseButtons button, Point screenPt)
        {
            if (IsBindingKey) return;

            Rectangle videoRect = _getVideoScreenRect();
            bool inVideo = videoRect.Contains(screenPt);

            if (Mode == ControlMode.Touch && _control.IsConnected)
            {
                if (button == MouseButtons.Left && inVideo)
                {
                    _touchDragging = true;
                    var devPt = ScreenToDevice(screenPt, videoRect);
                    _lastMoveDevPt = devPt;
                    _lastMoveTick = Environment.TickCount;
                    _control.InjectTouch(0, POINTER_TOUCH, devPt.X, devPt.Y, _host.VideoWidth, _host.VideoHeight);
                }
                else if (button == MouseButtons.Right && inVideo)
                {
                    _control.InjectTapKeycode(4); // Android BACK key on right-click in Touch mode
                }
            }
            else if (Mode == ControlMode.Play && !IsAimLocked && inVideo)
            {
                if (button == MouseButtons.Left)
                {
                    if (HandlePlayKeyDown(-1)) return;
                    if (_control.IsConnected)
                    {
                        _touchDragging = true;
                        var devPt = ScreenToDevice(screenPt, videoRect);
                        _lastMoveDevPt = devPt;
                        _lastMoveTick = Environment.TickCount;
                        _control.InjectTouch(0, POINTER_TOUCH, devPt.X, devPt.Y, _host.VideoWidth, _host.VideoHeight);
                    }
                }
                else if (button == MouseButtons.Right)
                {
                    HandlePlayKeyDown(-2);
                }
                else if (button == MouseButtons.Middle)
                {
                    HandlePlayKeyDown(-3);
                }
                else if (button == MouseButtons.XButton1)
                {
                    HandlePlayKeyDown(-4);
                }
                else if (button == MouseButtons.XButton2)
                {
                    HandlePlayKeyDown(-5);
                }
            }
            else if (Mode == ControlMode.Edit && inVideo)
            {
                if (button == MouseButtons.Left)
                {
                    var hit = HitTestItem(screenPt, videoRect);
                    if (hit != null)
                    {
                        _dragItem = hit;
                        _dragStartScreen = screenPt;
                        _dragMoved = false;
                    }
                }
                else if (button == MouseButtons.Right)
                {
                    var hit = HitTestItem(screenPt, videoRect);
                    if (hit != null) EditItemDeleted?.Invoke(hit);
                }
            }
        }

        public void HandleSurfaceMouseMove(Point screenPt)
        {
            if (IsBindingKey) return;
            Rectangle videoRect = _getVideoScreenRect();

            if ((Mode == ControlMode.Touch || (Mode == ControlMode.Play && !IsAimLocked)) && _touchDragging && _control.IsConnected)
            {
                var devPt = ScreenToDevice(screenPt, videoRect);
                int now = Environment.TickCount;
                if (Math.Abs(devPt.X - _lastMoveDevPt.X) >= 2 || Math.Abs(devPt.Y - _lastMoveDevPt.Y) >= 2 || unchecked(now - _lastMoveTick) >= 12)
                {
                    _lastMoveDevPt = devPt;
                    _lastMoveTick = now;
                    _control.InjectTouch(2, POINTER_TOUCH, devPt.X, devPt.Y, _host.VideoWidth, _host.VideoHeight);
                }
            }
            else if (Mode == ControlMode.Edit && _dragItem != null)
            {
                if (Math.Abs(screenPt.X - _dragStartScreen.X) > 4 || Math.Abs(screenPt.Y - _dragStartScreen.Y) > 4)
                    _dragMoved = true;

                if (_dragMoved)
                {
                    _dragItem.X = Math.Max(0.04, Math.Min(0.96, (screenPt.X - videoRect.Left) / (double)Math.Max(1, videoRect.Width)));
                    _dragItem.Y = Math.Max(0.04, Math.Min(0.96, (screenPt.Y - videoRect.Top) / (double)Math.Max(1, videoRect.Height)));
                    OverlayVisualChanged?.Invoke();
                }
            }
        }

        public void HandleSurfaceMouseUp(MouseButtons button, Point screenPt)
        {
            if (IsBindingKey) return;
            Rectangle videoRect = _getVideoScreenRect();

            if (Mode == ControlMode.Touch && _control.IsConnected)
            {
                if (button == MouseButtons.Left && _touchDragging)
                {
                    _touchDragging = false;
                    var devPt = ScreenToDevice(screenPt, videoRect);
                    _control.InjectTouch(1, POINTER_TOUCH, devPt.X, devPt.Y, _host.VideoWidth, _host.VideoHeight);
                }
            }
            else if (Mode == ControlMode.Play && !IsAimLocked)
            {
                if (button == MouseButtons.Left)
                {
                    if (HandlePlayKeyUp(-1)) return;
                    if (_touchDragging)
                    {
                        _touchDragging = false;
                        var devPt = ScreenToDevice(screenPt, videoRect);
                        _control.InjectTouch(1, POINTER_TOUCH, devPt.X, devPt.Y, _host.VideoWidth, _host.VideoHeight);
                    }
                }
                else if (button == MouseButtons.Right)
                {
                    HandlePlayKeyUp(-2);
                }
                else if (button == MouseButtons.Middle)
                {
                    HandlePlayKeyUp(-3);
                }
                else if (button == MouseButtons.XButton1)
                {
                    HandlePlayKeyUp(-4);
                }
                else if (button == MouseButtons.XButton2)
                {
                    HandlePlayKeyUp(-5);
                }
            }
            else if (Mode == ControlMode.Edit && button == MouseButtons.Left && _dragItem != null)
            {
                var item = _dragItem;
                bool wasMoved = _dragMoved;
                _dragItem = null;
                if (!wasMoved)
                {
                    EditItemClicked?.Invoke(item);
                }
            }
        }

        public void HandleSurfaceDoubleClick(Point screenPt)
        {
            if (Mode != ControlMode.Edit || IsBindingKey) return;
            Rectangle videoRect = _getVideoScreenRect();
            if (!videoRect.Contains(screenPt)) return;
            if (HitTestItem(screenPt, videoRect) != null) return;
            float nx = (float)Math.Max(0.04, Math.Min(0.96, (screenPt.X - videoRect.Left) / (double)Math.Max(1, videoRect.Width)));
            float ny = (float)Math.Max(0.04, Math.Min(0.96, (screenPt.Y - videoRect.Top) / (double)Math.Max(1, videoRect.Height)));
            EditEmptyDoubleClicked?.Invoke(new PointF(nx, ny));
        }

        public void HandleSurfaceMouseWheel(int delta, Point screenPt)
        {
            if (Mode != ControlMode.Touch || !_control.IsConnected || IsBindingKey) return;
            Rectangle videoRect = _getVideoScreenRect();
            if (!videoRect.Contains(screenPt)) return;
            var devPt = ScreenToDevice(screenPt, videoRect);
            _control.InjectScroll(devPt.X, devPt.Y, _host.VideoWidth, _host.VideoHeight, 0f, delta > 0 ? 1f : -1f);
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // When aim-locked in Play mode, intercept mouse clicks globally within the app
            if (nCode >= 0 && Mode == ControlMode.Play && IsAimLocked && IsAppActiveForeground() && !IsBindingKey)
            {
                int msg = wParam.ToInt32();
                if (msg == NativeMethods.WM_LBUTTONDOWN) { if (HandlePlayKeyDown(-1)) return (IntPtr)1; }
                else if (msg == NativeMethods.WM_LBUTTONUP) { if (HandlePlayKeyUp(-1)) return (IntPtr)1; }
                else if (msg == NativeMethods.WM_RBUTTONDOWN) { if (HandlePlayKeyDown(-2)) return (IntPtr)1; }
                else if (msg == NativeMethods.WM_RBUTTONUP) { if (HandlePlayKeyUp(-2)) return (IntPtr)1; }
                else if (msg == NativeMethods.WM_MBUTTONDOWN) { if (HandlePlayKeyDown(-3)) return (IntPtr)1; }
                else if (msg == NativeMethods.WM_MBUTTONUP) { if (HandlePlayKeyUp(-3)) return (IntPtr)1; }
                else if (msg == NativeMethods.WM_XBUTTONDOWN || msg == NativeMethods.WM_XBUTTONUP)
                {
                    var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                    int xbtn = (int)((info.mouseData >> 16) & 0xFFFF);
                    int vk = xbtn == 1 ? -4 : -5;
                    bool handled = msg == NativeMethods.WM_XBUTTONDOWN ? HandlePlayKeyDown(vk) : HandlePlayKeyUp(vk);
                    if (handled) return (IntPtr)1;
                }
            }

            return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        private KeymapItem HitTestItem(Point screenPt, Rectangle videoRect)
        {
            if (ActivePreset == null) return null;
            foreach (var item in ActivePreset.Items.AsEnumerable().Reverse())
            {
                double ix = videoRect.Left + item.X * videoRect.Width;
                double iy = videoRect.Top + item.Y * videoRect.Height;
                double radius = item.Type == "joystick"
                    ? Math.Max(36, item.Radius * Math.Min(videoRect.Width, videoRect.Height))
                    : item.Type == "aim" ? 34 : 26;
                if (Math.Sqrt(Math.Pow(screenPt.X - ix, 2) + Math.Pow(screenPt.Y - iy, 2)) <= radius)
                    return item;
            }
            return null;
        }

        private Point ScreenToDevice(Point screenPt, Rectangle videoRect)
        {
            double nx = Math.Max(0, Math.Min(1, (screenPt.X - videoRect.Left) / (double)Math.Max(1, videoRect.Width)));
            double ny = Math.Max(0, Math.Min(1, (screenPt.Y - videoRect.Top) / (double)Math.Max(1, videoRect.Height)));
            return new Point((int)Math.Round(nx * _host.VideoWidth), (int)Math.Round(ny * _host.VideoHeight));
        }

        private bool HandlePlayKeyDown(int vk)
        {
            if (ActivePreset == null) return false;

            // Check Aim lock toggle key
            var aim = ActivePreset.Items.FirstOrDefault(i => i.Type == "aim" && i.VirtualKey == vk);
            if (aim != null)
            {
                _mainForm.BeginInvoke(new Action(() => SetAimLock(!IsAimLocked)));
                return true;
            }

            // Check WASD
            int wasdIdx = VkToWasdIndex(vk);
            if (wasdIdx >= 0)
            {
                if (!_wasd[wasdIdx])
                {
                    _wasd[wasdIdx] = true;
                    UpdateJoystick();
                }
                return true;
            }

            // Check tap buttons
            int idx = ActivePreset.Items.FindIndex(i => i.Type == "tap" && i.VirtualKey == vk);
            if (idx < 0) return false;

            if (_activeTaps.ContainsKey(vk)) return true;
            var item = ActivePreset.Items[idx];
            int pointerId = POINTER_TAP_BASE + idx;
            int x = (int)Math.Round(item.X * _host.VideoWidth);
            int y = (int)Math.Round(item.Y * _host.VideoHeight);

            _activeTaps[vk] = new ActiveTap { PointerId = pointerId, X = x, Y = y, ItemId = item.Id };
            _control.InjectTouch(0, pointerId, x, y, _host.VideoWidth, _host.VideoHeight);
            PressedItemIds.Add(item.Id);
            OverlayVisualChanged?.Invoke();
            return true;
        }

        private bool HandlePlayKeyUp(int vk)
        {
            if (ActivePreset == null) return false;

            int wasdIdx = VkToWasdIndex(vk);
            if (wasdIdx >= 0)
            {
                if (_wasd[wasdIdx])
                {
                    _wasd[wasdIdx] = false;
                    UpdateJoystick();
                }
                return true;
            }

            ActiveTap tap;
            if (!_activeTaps.TryGetValue(vk, out tap)) return false;
            _activeTaps.Remove(vk);

            _control.InjectTouch(1, tap.PointerId, tap.X, tap.Y, _host.VideoWidth, _host.VideoHeight);
            PressedItemIds.Remove(tap.ItemId);
            OverlayVisualChanged?.Invoke();
            return true;
        }

        private static int VkToWasdIndex(int vk)
        {
            if (vk == 0x57) return 0; // W
            if (vk == 0x41) return 1; // A
            if (vk == 0x53) return 2; // S
            if (vk == 0x44) return 3; // D
            return -1;
        }

        private void UpdateJoystick()
        {
            var joy = ActivePreset?.Items.FirstOrDefault(i => i.Type == "joystick");
            if (joy == null) return;

            int dx = (_wasd[3] ? 1 : 0) - (_wasd[1] ? 1 : 0);
            int dy = (_wasd[2] ? 1 : 0) - (_wasd[0] ? 1 : 0);

            int cx = (int)Math.Round(joy.X * _host.VideoWidth);
            int cy = (int)Math.Round(joy.Y * _host.VideoHeight);
            int rPx = (int)Math.Round(joy.Radius * Math.Min(_host.VideoWidth, _host.VideoHeight));

            if (dx == 0 && dy == 0)
            {
                if (_joystickActive)
                {
                    _joystickActive = false;
                    _control.InjectTouch(1, POINTER_JOYSTICK, cx, cy, _host.VideoWidth, _host.VideoHeight);
                }
                JoystickOffset = PointF.Empty;
                OverlayVisualChanged?.Invoke();
                return;
            }

            double len = Math.Sqrt(dx * dx + dy * dy);
            double ndx = dx / len;
            double ndy = dy / len;
            int tx = (int)Math.Round(cx + ndx * rPx);
            int ty = (int)Math.Round(cy + ndy * rPx);

            if (!_joystickActive)
            {
                _joystickActive = true;
                _control.InjectTouch(0, POINTER_JOYSTICK, cx, cy, _host.VideoWidth, _host.VideoHeight);
            }
            _control.InjectTouch(2, POINTER_JOYSTICK, tx, ty, _host.VideoWidth, _host.VideoHeight);
            JoystickOffset = new PointF((float)ndx, (float)ndy);
            OverlayVisualChanged?.Invoke();
        }

        public void SetAimLock(bool lockOn)
        {
            var aim = ActivePreset?.Items.FirstOrDefault(i => i.Type == "aim");
            if (lockOn && aim != null && Mode == ControlMode.Play)
            {
                if (IsAimLocked) return;
                Rectangle rect = _getVideoScreenRect();
                if (rect.Width < 20 || rect.Height < 20) return;
                var nativeRect = new NativeMethods.RECT
                {
                    Left = rect.Left + 8,
                    Top = rect.Top + 8,
                    Right = rect.Right - 8,
                    Bottom = rect.Bottom - 8
                };
                NativeMethods.ClipCursor(ref nativeRect);
                // Lock Windows cursor near the configured aim center inside the video viewport
                int lockScrX = Math.Max(nativeRect.Left, Math.Min(nativeRect.Right, rect.Left + (int)Math.Round(aim.X * rect.Width)));
                int lockScrY = Math.Max(nativeRect.Top, Math.Min(nativeRect.Bottom, rect.Top + (int)Math.Round(aim.Y * rect.Height)));
                NativeMethods.SetCursorPos(lockScrX, lockScrY);
                if (!_cursorHidden)
                {
                    NativeMethods.ShowCursor(false);
                    _cursorHidden = true;
                }

                IsAimLocked = true;
                _aimCurX = (int)Math.Round(aim.X * _host.VideoWidth);
                _aimCurY = (int)Math.Round(aim.Y * _host.VideoHeight);
                _control.InjectTouch(0, POINTER_AIM, _aimCurX, _aimCurY, _host.VideoWidth, _host.VideoHeight);
            }
            else
            {
                if (!IsAimLocked) return;
                IsAimLocked = false;
                NativeMethods.ClipCursor(IntPtr.Zero);
                if (_cursorHidden)
                {
                    NativeMethods.ShowCursor(true);
                    _cursorHidden = false;
                }
                _control.InjectTouch(1, POINTER_AIM, _aimCurX, _aimCurY, _host.VideoWidth, _host.VideoHeight);
            }
            AimLockChanged?.Invoke(IsAimLocked);
            OverlayVisualChanged?.Invoke();
        }

        private void HandleAimDelta(int dx, int dy)
        {
            var aim = ActivePreset?.Items.FirstOrDefault(i => i.Type == "aim");
            if (aim == null) return;

            int stepX = (int)Math.Round(dx * MouseSensitivity * 1.5);
            int stepY = (int)Math.Round(dy * MouseSensitivity * 1.5);
            _aimCurX += stepX;
            _aimCurY += stepY;

            // Keep safe swipe box centered dynamically around the user-configured (aim.X, aim.Y)
            int cx = (int)Math.Round(aim.X * _host.VideoWidth);
            int cy = (int)Math.Round(aim.Y * _host.VideoHeight);
            int halfW = Math.Max(80, (int)Math.Round(_host.VideoWidth * 0.22));
            int halfH = Math.Max(80, (int)Math.Round(_host.VideoHeight * 0.26));
            int minX = Math.Max((int)Math.Round(_host.VideoWidth * 0.05), cx - halfW);
            int maxX = Math.Min((int)Math.Round(_host.VideoWidth * 0.95), cx + halfW);
            int minY = Math.Max((int)Math.Round(_host.VideoHeight * 0.06), cy - halfH);
            int maxY = Math.Min((int)Math.Round(_host.VideoHeight * 0.94), cy + halfH);

            if (_aimCurX < minX || _aimCurX > maxX || _aimCurY < minY || _aimCurY > maxY)
            {
                _control.InjectTouch(1, POINTER_AIM, _aimCurX, _aimCurY, _host.VideoWidth, _host.VideoHeight);
                _control.InjectTouch(0, POINTER_AIM, cx, cy, _host.VideoWidth, _host.VideoHeight);
                _aimCurX = cx + stepX;
                _aimCurY = cy + stepY;
                _control.InjectTouch(2, POINTER_AIM, _aimCurX, _aimCurY, _host.VideoWidth, _host.VideoHeight);
                return;
            }

            _control.InjectTouch(2, POINTER_AIM, _aimCurX, _aimCurY, _host.VideoWidth, _host.VideoHeight);
        }

        public void ReleaseAllInputs()
        {
            SetAimLock(false);
            if (_touchDragging)
            {
                _touchDragging = false;
                _control.InjectTouch(1, POINTER_TOUCH, _lastMoveDevPt.X, _lastMoveDevPt.Y, _host.VideoWidth, _host.VideoHeight);
            }
            Array.Clear(_wasd, 0, _wasd.Length);
            if (_joystickActive)
            {
                _joystickActive = false;
                var joy = ActivePreset?.Items.FirstOrDefault(i => i.Type == "joystick");
                if (joy != null)
                    _control.InjectTouch(1, POINTER_JOYSTICK, (int)(joy.X * _host.VideoWidth), (int)(joy.Y * _host.VideoHeight), _host.VideoWidth, _host.VideoHeight);
            }
            foreach (var tap in _activeTaps.Values.ToList())
            {
                _control.InjectTouch(1, tap.PointerId, tap.X, tap.Y, _host.VideoWidth, _host.VideoHeight);
            }
            _activeTaps.Clear();
            PressedItemIds.Clear();
            JoystickOffset = PointF.Empty;
            OverlayVisualChanged?.Invoke();
        }

        public void Dispose()
        {
            ReleaseAllInputs();
            if (_kbHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_kbHook);
            if (_mouseHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _kbHook = IntPtr.Zero;
            _mouseHook = IntPtr.Zero;
        }
    }
}
