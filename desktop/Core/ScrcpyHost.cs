using System;
using System.Diagnostics;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DroidDeck.Core
{
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

        public event Action<int, int> VideoDimensionsChanged;
        public event Action<int> FpsUpdated;
        public event Action<string> StatusTextChanged;
        public event Action<bool> RunningStateChanged;

        private int _maxSize = 1920;
        private int _maxFps = 120;
        private int _bitRateM = 16;
        private bool _enableAudio = true;
        private bool _turnScreenOff = false;
        private bool _antiAlias = true;
        private string _serial;
        private bool _isReconnecting;
        private string _lastErrorLine;

        public ScrcpyHost(AdbManager adb, Control hostPanel)
        {
            _adb = adb;
            _hostPanel = hostPanel;
        }

        public async Task StartAsync(string serial, int maxSize, int maxFps, int bitRateM, bool enableAudio, bool turnScreenOff, bool antiAlias = true)
        {
            StopInternal(false);
            WantRunning = true;
            _serial = serial;
            _maxSize = maxSize;
            _maxFps = maxFps;
            _bitRateM = bitRateM;
            _enableAudio = enableAudio;
            _turnScreenOff = turnScreenOff;
            _antiAlias = antiAlias;

            await LaunchProcessAsync();
        }

        private async Task LaunchProcessAsync()
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _lastErrorLine = null;

            // Keep native scrcpy control enabled so Touch mode gets 1:1 SDL3 clicks/swipes/scrolls directly,
            // while Play/Edit modes intercept via OverlayForm & RawInput/hooks above it.
            // OpenGL renderer enables trilinear mipmap filtering to eliminate downscale jaggedness.
            string audioFlag = _enableAudio ? "" : "--no-audio";
            string sizeFlag = _maxSize > 0 ? $"--max-size={_maxSize}" : "";
            string renderFlag = _antiAlias ? "--render-driver=opengl" : "";
            string args = $"-s {_serial} --window-borderless --window-title=DroidDeckVideo {sizeFlag} --max-fps={_maxFps} --video-bit-rate={_bitRateM}M --stay-awake --print-fps {renderFlag} {audioFlag}".Trim();

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

            // Wait for scrcpy SDL3 window handle and embed inside _hostPanel
            bool embedded = false;
            for (int i = 0; i < 80; i++)
            {
                if (token.IsCancellationRequested || proc.HasExited) break;
                await Task.Delay(100, token);
                proc.Refresh();
                if (proc.MainWindowHandle != IntPtr.Zero)
                {
                    _scrcpyHwnd = proc.MainWindowHandle;
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
            if (embedded)
            {
                RunningStateChanged?.Invoke(true);
                string renderLabel = _antiAlias ? "OpenGL 抗锯齿" : "D3D11VA 硬解";
                StatusTextChanged?.Invoke($"🟢 原生 {renderLabel}投屏中 ({_maxFps} FPS / {_bitRateM}M)");
            }
            else if (!proc.HasExited)
            {
                RunningStateChanged?.Invoke(true);
                string renderLabel = _antiAlias ? "OpenGL 抗锯齿" : "D3D11VA 硬解";
                StatusTextChanged?.Invoke($"🟢 原生 {renderLabel}投屏中 ({_maxFps} FPS / {_bitRateM}M)");
            }
        }

        private void ParseScrcpyLog(string line)
        {
            if (line.IndexOf("ERROR:", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _lastErrorLine = line.Trim();
            }

            // Match "Texture (D3D11VA): 864x1920" or "New texture: 1920x864"
            var texMatch = Regex.Match(line, @"Texture.*?:\s*(\d+)x(\d+)", RegexOptions.IgnoreCase);
            if (texMatch.Success)
            {
                int w = int.Parse(texMatch.Groups[1].Value);
                int h = int.Parse(texMatch.Groups[2].Value);
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
