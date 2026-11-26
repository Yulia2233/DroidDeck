using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace DroidDeck.Core
{
    public class ScrcpyControlChannel : IDisposable
    {
        private readonly AdbManager _adb;
        private Process _serverProc;
        private TcpClient _tcpClient;
        private NetworkStream _stream;
        private readonly object _writeLock = new object();
        private CancellationTokenSource _cts;
        private int _port;
        private string _serial;
        private bool _connected;

        public bool IsConnected => _connected && _stream != null;
        public event Action Disconnected;

        public ScrcpyControlChannel(AdbManager adb)
        {
            _adb = adb;
        }

        public async Task StartAsync(string serial)
        {
            Stop();
            _serial = serial;
            _cts = new CancellationTokenSource();
            var rnd = new Random();
            int scidNum = rnd.Next(0x1000000, 0x7fffffff);
            string scid = scidNum.ToString("x8");
            _port = 27210 + rnd.Next(0, 60);

            Exception prepErr = null;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    await _adb.RunAdbAsync($"-s {serial} push \"{_adb.ScrcpyServerPath}\" /data/local/tmp/scrcpy-server.jar", 10000);
                    await _adb.RunAdbAsync($"-s {serial} forward tcp:{_port} localabstract:scrcpy_{scid}", 5000);
                    prepErr = null;
                    break;
                }
                catch (Exception ex)
                {
                    prepErr = ex;
                    if (attempt < 3)
                    {
                        try { await _adb.RunAdbAsync("reconnect offline", 3000); } catch { }
                        await Task.Delay(600);
                        try { await _adb.RunAdbAsync("wait-for-device", 4000); } catch { }
                    }
                }
            }
            if (prepErr != null) throw prepErr;

            string serverCmd = $"CLASSPATH=/data/local/tmp/scrcpy-server.jar app_process / com.genymobile.scrcpy.Server 5.0.1 scid={scid} log_level=info tunnel_forward=true video=false audio=false control=true stay_awake=true power_on=true clipboard_autosync=false cleanup=false";
            var psi = new ProcessStartInfo
            {
                FileName = _adb.AdbPath,
                Arguments = $"-s {serial} shell {serverCmd}",
                WorkingDirectory = _adb.ScrcpyDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            AdbManager.ApplyStableAdbEnv(psi);

            _serverProc = Process.Start(psi);
            string lastServerErr = null;
            _serverProc.EnableRaisingEvents = true;
            _serverProc.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data)) lastServerErr = e.Data.Trim();
            };
            _serverProc.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data) && e.Data.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) >= 0)
                    lastServerErr = e.Data.Trim();
            };
            _serverProc.BeginErrorReadLine();
            _serverProc.BeginOutputReadLine();
            _serverProc.Exited += (s, e) =>
            {
                if (_connected)
                {
                    _connected = false;
                    Disconnected?.Invoke();
                }
            };

            // Connect TCP socket to localabstract:scrcpy_<scid>
            bool gotDummy = false;
            for (int i = 0; i < 35; i++)
            {
                if (_cts.IsCancellationRequested || _serverProc.HasExited) break;
                await Task.Delay(120);
                try
                {
                    var client = new TcpClient();
                    client.NoDelay = true;
                    await client.ConnectAsync("127.0.0.1", _port);
                    var ns = client.GetStream();
                    ns.ReadTimeout = 3000;
                    int b = ns.ReadByte();
                    if (b >= 0)
                    {
                        ns.ReadTimeout = Timeout.Infinite;
                        _tcpClient = client;
                        _stream = ns;
                        _connected = true;
                        gotDummy = true;
                        break;
                    }
                    client.Close();
                }
                catch { }
            }

            if (!gotDummy)
            {
                string detail = !string.IsNullOrEmpty(lastServerErr) ? $" ({lastServerErr})" : "";
                Stop();
                throw new Exception("控制通道连接超时" + detail);
            }

            // Drain incoming device messages asynchronously so socket never stalls
            _ = Task.Run(async () =>
            {
                byte[] buf = new byte[4096];
                try
                {
                    while (_connected && _stream != null && !_cts.IsCancellationRequested)
                    {
                        int n = await _stream.ReadAsync(buf, 0, buf.Length, _cts.Token);
                        if (n <= 0) break;
                    }
                }
                catch { }
                if (_connected)
                {
                    _connected = false;
                    Disconnected?.Invoke();
                }
            });
        }

        public void InjectTouch(byte action, long pointerId, int x, int y, int width, int height)
        {
            if (!_connected || _stream == null) return;
            int w = Math.Max(1, width);
            int h = Math.Max(1, height);
            int cx = Math.Max(0, Math.Min(w - 1, x));
            int cy = Math.Max(0, Math.Min(h - 1, y));

            // Scrcpy TYPE_INJECT_TOUCH_EVENT = 2 (32 bytes, big-endian)
            byte[] msg = new byte[32];
            msg[0] = 2;
            msg[1] = action; // 0=DOWN, 1=UP, 2=MOVE
            WriteInt64BE(msg, 2, pointerId);
            WriteInt32BE(msg, 10, cx);
            WriteInt32BE(msg, 14, cy);
            WriteUInt16BE(msg, 18, (ushort)w);
            WriteUInt16BE(msg, 20, (ushort)h);
            WriteUInt16BE(msg, 22, (ushort)(action == 1 ? 0x0000 : 0xFFFF));
            WriteInt32BE(msg, 24, 1);
            WriteInt32BE(msg, 28, action == 1 ? 0 : 1);

            WriteBytes(msg);
        }

        public void InjectKeycode(byte action, int keycode)
        {
            if (!_connected || _stream == null) return;
            byte[] msg = new byte[14];
            msg[0] = 0; // TYPE_INJECT_KEYCODE
            msg[1] = action; // 0=DOWN, 1=UP
            WriteInt32BE(msg, 2, keycode);
            WriteInt32BE(msg, 6, 0);
            WriteInt32BE(msg, 10, 0);
            WriteBytes(msg);
        }

        public void InjectTapKeycode(int keycode)
        {
            InjectKeycode(0, keycode);
            Task.Run(async () =>
            {
                await Task.Delay(35);
                InjectKeycode(1, keycode);
            });
        }

        public void InjectScroll(int x, int y, int width, int height, float hScroll, float vScroll)
        {
            if (!_connected || _stream == null) return;
            int w = Math.Max(1, width);
            int h = Math.Max(1, height);
            int cx = Math.Max(0, Math.Min(w - 1, x));
            int cy = Math.Max(0, Math.Min(h - 1, y));

            byte[] msg = new byte[21];
            msg[0] = 3; // TYPE_INJECT_SCROLL_EVENT
            WriteInt32BE(msg, 1, cx);
            WriteInt32BE(msg, 5, cy);
            WriteUInt16BE(msg, 9, (ushort)w);
            WriteUInt16BE(msg, 11, (ushort)h);
            short hs = (short)Math.Max(-32768, Math.Min(32767, (int)(hScroll * 2048)));
            short vs = (short)Math.Max(-32768, Math.Min(32767, (int)(vScroll * 2048)));
            WriteUInt16BE(msg, 13, (ushort)hs);
            WriteUInt16BE(msg, 15, (ushort)vs);
            WriteInt32BE(msg, 17, 0);
            WriteBytes(msg);
        }

        public void SetDisplayPower(bool on)
        {
            if (!_connected || _stream == null) return;
            byte[] msg = new byte[2];
            msg[0] = 10; // TYPE_SET_DISPLAY_POWER
            msg[1] = (byte)(on ? 1 : 0);
            WriteBytes(msg);
        }

        private void WriteBytes(byte[] data)
        {
            lock (_writeLock)
            {
                try
                {
                    if (_connected && _stream != null)
                        _stream.Write(data, 0, data.Length);
                }
                catch
                {
                    _connected = false;
                }
            }
        }

        private static void WriteUInt16BE(byte[] buf, int offset, ushort val)
        {
            buf[offset] = (byte)(val >> 8);
            buf[offset + 1] = (byte)(val & 0xFF);
        }

        private static void WriteInt32BE(byte[] buf, int offset, int val)
        {
            buf[offset] = (byte)((val >> 24) & 0xFF);
            buf[offset + 1] = (byte)((val >> 16) & 0xFF);
            buf[offset + 2] = (byte)((val >> 8) & 0xFF);
            buf[offset + 3] = (byte)(val & 0xFF);
        }

        private static void WriteInt64BE(byte[] buf, int offset, long val)
        {
            for (int i = 0; i < 8; i++)
                buf[offset + i] = (byte)((val >> (56 - i * 8)) & 0xFF);
        }

        public void Stop()
        {
            _connected = false;
            try { _cts?.Cancel(); } catch { }
            try { _stream?.Dispose(); } catch { }
            try { _tcpClient?.Close(); } catch { }
            _stream = null;
            _tcpClient = null;

            if (_serverProc != null)
            {
                try { if (!_serverProc.HasExited) _serverProc.Kill(); } catch { }
                _serverProc = null;
            }
            if (!string.IsNullOrEmpty(_serial) && _port > 0)
            {
                try { _adb.RunAdbSync($"-s {_serial} forward --remove tcp:{_port}", 1500); } catch { }
            }
        }

        public void Dispose() => Stop();
    }
}
