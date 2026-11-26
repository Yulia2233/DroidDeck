using System;
using System.Diagnostics;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DroidDeck.Core
{
    public enum StreamEngineProfile
    {
        OpenGlAntiAliasH264 = 0,
        HighFpsHevcD3d11 = 1,
        BalancedH264D3d11 = 2,
        HighFpsHevcOpenGl = 3
    }

    public class ScrcpyHost : IDisposable
    {
        private readonly AdbManager _adb;
        private readonly Control _hostPanel;
        private Process _scrcpyProc;
        private IntPtr _scrcpyHwnd = IntPtr.Zero;
        private CancellationTokenSource _cts;

        public bool IsRunning => _scrcpyProc != null && !_scrcpyProc.HasExited;
        public bool WantRunning { get; private set; }
        public IntPtr EmbeddedHwnd => _scrcpyHwnd;
        public int ScrcpyProcessId => _scrcpyProc != null && !_scrcpyProc.HasExited ? _scrcpyProc.Id : 0;
        public int VideoWidth { get; private set; } = 1200;
        public int VideoHeight { get; private set; } = 2670;

        /// <summary>
        /// scrcpy-server control channel (video=false) has displayData == null,
        /// so Controller.getEventPointAndDisplayId injects raw physical display pixels
        /// matching current device orientation rather than scaled video texture pixels.
        /// </summary>
        public int ControlWidth
        {
            get
            {
                var st = _adb.CurrentStatus;
                int pw = st != null && st.PhysicalWidth > 0 ? st.PhysicalWidth : 1200;
                int ph = st != null && st.PhysicalHeight > 0 ? st.PhysicalHeight : 2670;
                return VideoWidth > VideoHeight ? Math.Max(pw, ph) : Math.Min(pw, ph);
            }
        }

        public int ControlHeight
        {
            get
            {
                var st = _adb.CurrentStatus;
                int pw = st != null && st.PhysicalWidth > 0 ? st.PhysicalWidth : 1200;
                int ph = st != null && st.PhysicalHeight > 0 ? st.PhysicalHeight : 2670;
                return VideoWidth > VideoHeight ? Math.Min(pw, ph) : Math.Max(pw, ph);
            }
        }

        public event Action<int, int> VideoDimensionsChanged;
        public event Action<int> FpsUpdated;
        public event Action<string> StatusTextChanged;
        public event Action<bool> RunningStateChanged;

        private int _maxSize = 1920;
        private int _maxFps = 120;
        private int _bitRateM = 16;
        private bool _enableAudio = true;
        private bool _turnScreenOff = false;
        private StreamEngineProfile _engineProfile = StreamEngineProfile.OpenGlAntiAliasH264;
        private string _serial;
        private bool _isReconnecting;
        private string _lastErrorLine;

        public ScrcpyHost(AdbManager adb, Control hostPanel)
        {
            _adb = adb;
            _hostPanel = hostPanel;
        }

        public async Task StartAsync(string serial, int maxSize, int maxFps, int bitRateM, bool enableAudio, bool turnScreenOff, StreamEngineProfile engineProfile = StreamEngineProfile.OpenGlAntiAliasH264)
        {
            StopInternal(false);
            WantRunning = true;
            _serial = serial;
            _maxSize = maxSize;
            _maxFps = maxFps;
            _bitRateM = bitRateM;
            _enableAudio = enableAudio;
            _turnScreenOff = turnScreenOff;
            _engineProfile = engineProfile;

            await LaunchProcessAsync();
        }

        private async Task LaunchProcessAsync()
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _lastErrorLine = null;

            bool useHevc = _engineProfile == StreamEngineProfile.HighFpsHevcD3d11 || _engineProfile == StreamEngineProfile.HighFpsHevcOpenGl;
            bool useOpenGl = _engineProfile == StreamEngineProfile.OpenGlAntiAliasH264 || _engineProfile == StreamEngineProfile.HighFpsHevcOpenGl;
            string audioFlag = _enableAudio ? "--audio-buffer=30" : "--no-audio";
            string sizeFlag = _maxSize > 0 ? $"--max-size={_maxSize}" : "";
            string codecFlags = useHevc ? "--video-codec=h265" : "";
            string renderFlag = useOpenGl ? "--render-driver=opengl" : "";
            string rawArgs = $"-s {_serial} --window-borderless --window-title=DroidDeckVideo {sizeFlag} --max-fps={_maxFps} --video-bit-rate={_bitRateM}M --stay-awake --print-fps {codecFlags} {renderFlag} {audioFlag}";
            string args = Regex.Replace(rawArgs, @"\s+", " ").Trim();

            var psi = new ProcessStartInfo
            {
                FileName = _adb.ScrcpyPath,
                Arguments = args,
                WorkingDirectory = _adb.ScrcpyDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            AdbManager.ApplyStableAdbEnv(psi);

            var proc = Process.Start(psi);
            _scrcpyProc = proc;
            _scrcpyHwnd = IntPtr.Zero;
            proc.EnableRaisingEvents = true;

            proc.OutputDataReceived += (s, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                ParseScrcpyLog(e.Data);
            };
            proc.ErrorDataReceived += (s, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                ParseScrcpyLog(e.Data);
            };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            bool initialPhaseDone = false;
            proc.Exited += (s, e) =>
            {
                if (_scrcpyProc != proc) return;
                _scrcpyHwnd = IntPtr.Zero;
                RunningStateChanged?.Invoke(false);
                if (initialPhaseDone && WantRunning && !_isReconnecting)
                {
                    _ = AutoReconnectLoopAsync();
                }
            };

            // Wait for scrcpy SDL3 main video HWND (skipping 50x50 D3D11VA / WGL probe windows)
            bool embedded = false;
            for (int i = 0; i < 80; i++)
            {
                if (token.IsCancellationRequested || proc.HasExited) break;
                await Task.Delay(100, token);
                proc.Refresh();
                IntPtr foundHwnd = FindScrcpyMainWindow(proc);
                if (foundHwnd != IntPtr.Zero)
                {
                    _scrcpyHwnd = foundHwnd;
                    EmbedWindow();
                    embedded = true;
                    break;
                }
            }

            if (proc.HasExited)
            {
                WantRunning = false;
                await Task.Delay(80);
                string reason = !string.IsNullOrEmpty(_lastErrorLine)
                    ? _lastErrorLine
                    : $"scrcpy 进程意外退出 (ExitCode={proc.ExitCode})";
                throw new Exception(reason);
            }

            initialPhaseDone = true;
            if (embedded || !proc.HasExited)
            {
                RunningStateChanged?.Invoke(true);
                StatusTextChanged?.Invoke(GetRenderStatusText());
            }
        }

        private string GetRenderStatusText()
        {
            switch (_engineProfile)
            {
                case StreamEngineProfile.HighFpsHevcD3d11:
                    return $"⚡ H.265 硬解投屏 (HEVC / D3D11VA / {_maxFps} FPS / {_bitRateM}M)";
                case StreamEngineProfile.BalancedH264D3d11:
                    return $"🚀 H.264 硬解投屏 (D3D11VA / {_maxFps} FPS / {_bitRateM}M)";
                case StreamEngineProfile.HighFpsHevcOpenGl:
                    return $"💎 H.265 抗锯齿投屏 (HEVC / OpenGL / {_maxFps} FPS / {_bitRateM}M)";
                default:
                    return $"🟢 OpenGL 低延迟抗锯齿 (H.264 / {_maxFps} FPS / {_bitRateM}M)";
            }
        }

        private static IntPtr FindScrcpyMainWindow(Process proc)
        {
            if (proc == null || proc.HasExited) return IntPtr.Zero;
            int targetPid = proc.Id;
            IntPtr matched = IntPtr.Zero;
            NativeMethods.EnumWindows((hwnd, lParam) =>
            {
                uint pid;
                NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
                if (pid != targetPid || !NativeMethods.IsWindowVisible(hwnd)) return true;

                NativeMethods.RECT r;
                if (!NativeMethods.GetWindowRect(hwnd, out r)) return true;
                if ((r.Right - r.Left) <= 60 || (r.Bottom - r.Top) <= 60) return true;

                var sb = new System.Text.StringBuilder(128);
                NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
                string title = sb.ToString();
                if (string.Equals(title, "DroidDeckVideo", StringComparison.OrdinalIgnoreCase))
                {
                    matched = hwnd;
                    return false;
                }
                if (matched == IntPtr.Zero && !string.IsNullOrEmpty(title))
                {
                    matched = hwnd;
                }
                return true;
            }, IntPtr.Zero);

            if (matched != IntPtr.Zero) return matched;

            if (proc.MainWindowHandle != IntPtr.Zero)
            {
                NativeMethods.RECT mr;
                if (NativeMethods.GetWindowRect(proc.MainWindowHandle, out mr) &&
                    (mr.Right - mr.Left) > 60 && (mr.Bottom - mr.Top) > 60)
                {
                    return proc.MainWindowHandle;
                }
            }
            return IntPtr.Zero;
        }

        private void ParseScrcpyLog(string line)
        {
            if (line.IndexOf("ERROR:", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _lastErrorLine = line.Trim();
            }

            // Match "Texture (D3D11VA): 896x1920" or "Texture: 862x1920"
            var texMatch = Regex.Match(line, @"Texture.*?:\s*(\d+)x(\d+)", RegexOptions.IgnoreCase);
            if (texMatch.Success)
            {
                int w = int.Parse(texMatch.Groups[1].Value);
                int h = int.Parse(texMatch.Groups[2].Value);
                if (w > 0 && h > 0 && line.IndexOf("(D3D11VA)", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // D3D11VA texture dimensions are hardware-aligned (e.g. 64px CTU padding 862->896 in HEVC);
                    // derive true unpadded content dimensions so aspect ratio & overlay match scrcpy's viewport.
                    var st = _adb.CurrentStatus;
                    int pw = st != null && st.PhysicalWidth > 0 ? st.PhysicalWidth : 1200;
                    int ph = st != null && st.PhysicalHeight > 0 ? st.PhysicalHeight : 2670;
                    bool isLandscape = w > h;
                    int physLong = Math.Max(pw, ph);
                    int physShort = Math.Min(pw, ph);
                    int contentLong = _maxSize > 0 ? Math.Min(_maxSize, physLong) : physLong;
                    int contentShort = Math.Max(2, (int)Math.Round(contentLong * (physShort / (double)physLong) / 2.0) * 2);
                    w = isLandscape ? contentLong : contentShort;
                    h = isLandscape ? contentShort : contentLong;
                }
                if (w > 0 && h > 0 && (w != VideoWidth || h != VideoHeight))
                {
                    VideoWidth = w;
                    VideoHeight = h;
                    if (_hostPanel.IsHandleCreated)
                    {
                        _hostPanel.BeginInvoke(new Action(() =>
                        {
                            LayoutEmbeddedWindow();
                            VideoDimensionsChanged?.Invoke(w, h);
                        }));
                    }
                }
            }

            var fpsMatch = Regex.Match(line, @"(\d+)\s+fps\b", RegexOptions.IgnoreCase);
            if (fpsMatch.Success)
            {
                int fps;
                if (int.TryParse(fpsMatch.Groups[1].Value, out fps))
                {
                    FpsUpdated?.Invoke(fps);
                }
            }
        }

        private void EmbedWindow()
        {
            if (_scrcpyHwnd == IntPtr.Zero || _hostPanel.IsDisposed) return;
            if (_hostPanel.InvokeRequired)
            {
                _hostPanel.BeginInvoke(new Action(EmbedWindow));
                return;
            }
            NativeMethods.SetParent(_scrcpyHwnd, _hostPanel.Handle);
            NativeMethods.StripWindowChrome(_scrcpyHwnd);
            LayoutEmbeddedWindow();
            _hostPanel.FindForm()?.Activate();
        }

        public Rectangle GetFittedVideoRect()
        {
            int pw = Math.Max(1, _hostPanel.ClientSize.Width);
            int ph = Math.Max(1, _hostPanel.ClientSize.Height);
            double ratio = (double)VideoWidth / Math.Max(1, VideoHeight);

            int targetW = (int)Math.Round(ph * ratio);
            int targetH = ph;
            if (targetW > pw)
            {
                targetW = pw;
                targetH = (int)Math.Round(pw / ratio);
            }

            int x = (pw - targetW) / 2;
            int y = (ph - targetH) / 2;
            return new Rectangle(x, y, Math.Max(1, targetW), Math.Max(1, targetH));
        }

        public void LayoutEmbeddedWindow()
        {
            if (_scrcpyHwnd == IntPtr.Zero || !NativeMethods.IsWindow(_scrcpyHwnd)) return;
            Rectangle r = GetFittedVideoRect();
            NativeMethods.MoveWindow(_scrcpyHwnd, r.X, r.Y, r.Width, r.Height, true);
        }

        private async Task AutoReconnectLoopAsync()
        {
            _isReconnecting = true;
            StatusTextChanged?.Invoke("🟡 USB 连接波动，正在自动重连...");
            try
            {
                for (int attempt = 0; attempt < 20 && WantRunning; attempt++)
                {
                    await Task.Delay(1200);
                    var st = await _adb.PollStatusAsync(true);
                    if (st.AdbState == "device" && !string.IsNullOrEmpty(st.Serial))
                    {
                        _serial = st.Serial;
                        try
                        {
                            await LaunchProcessAsync();
                            return;
                        }
                        catch { }
                    }
                }
                WantRunning = false;
                StatusTextChanged?.Invoke("⏹ 连接已断开，请检查 Type-C 数据线");
            }
            finally
            {
                _isReconnecting = false;
            }
        }

        private void StopInternal(bool userRequested)
        {
            if (userRequested) WantRunning = false;
            try { _cts?.Cancel(); } catch { }
            if (_scrcpyProc != null)
            {
                var p = _scrcpyProc;
                _scrcpyProc = null;
                try { if (!p.HasExited) p.Kill(); } catch { }
            }
            _scrcpyHwnd = IntPtr.Zero;
        }

        public void Stop()
        {
            StopInternal(true);
            RunningStateChanged?.Invoke(false);
            StatusTextChanged?.Invoke("⏹ 待启动投屏");
        }

        public void Dispose() => Stop();
    }
}
