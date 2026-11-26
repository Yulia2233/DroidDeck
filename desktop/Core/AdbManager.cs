using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DroidDeck.Core
{
    public class DeviceStatus
    {
        public string AdbState { get; set; } = "disconnected"; // disconnected | unauthorized | offline | device
        public string Serial { get; set; }
        public string Model { get; set; }
        public string MarketName { get; set; }
        public string HyperOsVersion { get; set; }
        public string AndroidVersion { get; set; }
        public string Resolution { get; set; }
        public int PhysicalWidth { get; set; } = 1200;
        public int PhysicalHeight { get; set; } = 2670;
        public int? BatteryLevel { get; set; }
        public bool UsbDetected { get; set; }
    }

    public class AdbManager
    {
        public string ScrcpyDir { get; }
        public string AdbPath { get; }
        public string ScrcpyPath { get; }
        public string ScrcpyServerPath { get; }

        private string _cachedSerial;
        private DeviceStatus _lastStatus = new DeviceStatus();
        private DateTime _lastBatteryAt = DateTime.MinValue;
        private DateTime _lastHealAt = DateTime.MinValue;
        private int _consecutiveDisconnected;
        private int _isPolling;

        public DeviceStatus CurrentStatus => _lastStatus;

        public AdbManager()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string candidate = Path.Combine(baseDir, "bin", "scrcpy");
            if (!File.Exists(Path.Combine(candidate, "adb.exe")))
            {
                // Fallback when running from desktop\build\ during dev
                string devCandidate = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "bin", "scrcpy"));
                if (File.Exists(Path.Combine(devCandidate, "adb.exe")))
                {
                    candidate = devCandidate;
                }
            }
            ScrcpyDir = candidate;
            AdbPath = Path.Combine(ScrcpyDir, "adb.exe");
            ScrcpyPath = Path.Combine(ScrcpyDir, "scrcpy.exe");
            ScrcpyServerPath = Path.Combine(ScrcpyDir, "scrcpy-server");
        }

        public static void ApplyStableAdbEnv(ProcessStartInfo psi)
        {
            psi.EnvironmentVariables["ADB_LIBUSB"] = "1";
            psi.EnvironmentVariables["ADB_BURST_MODE"] = "0";
            psi.EnvironmentVariables["ADB_MDNS_AUTO_CONNECT"] = "0";
        }

        public async Task<string> RunAdbAsync(string args, int timeoutMs = 8000)
        {
            return await Task.Run(() => RunAdbSync(args, timeoutMs));
        }

        public string RunAdbSync(string args, int timeoutMs = 8000)
        {
            if (!File.Exists(AdbPath))
                throw new FileNotFoundException("未找到内置 adb.exe: " + AdbPath);

            var psi = new ProcessStartInfo
            {
                FileName = AdbPath,
                Arguments = args,
                WorkingDirectory = ScrcpyDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            ApplyStableAdbEnv(psi);

            using (var proc = Process.Start(psi))
            {
                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();
                if (!proc.WaitForExit(timeoutMs))
                {
                    try { proc.Kill(); } catch { }
                    throw new TimeoutException("ADB 命令超时: " + args);
                }
                if (proc.ExitCode != 0)
                {
                    string err = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                    throw new Exception(err.Trim());
                }
                return stdout;
            }
        }

        public byte[] RunAdbBinarySync(string args, int timeoutMs = 8000)
        {
            var psi = new ProcessStartInfo
            {
                FileName = AdbPath,
                Arguments = args,
                WorkingDirectory = ScrcpyDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            ApplyStableAdbEnv(psi);

            using (var proc = Process.Start(psi))
            using (var ms = new MemoryStream())
            {
                proc.StandardOutput.BaseStream.CopyTo(ms);
                if (!proc.WaitForExit(timeoutMs))
                {
                    try { proc.Kill(); } catch { }
                    throw new TimeoutException("ADB 截图超时");
                }
                return ms.ToArray();
            }
        }

        public async Task EnsureAdbReadyAsync()
        {
            try
            {
                string features = await RunAdbAsync("host-features", 2500);
                if (features.Contains("libusb")) return;
            }
            catch { }
            await RestartAdbAsync();
        }

        public async Task RestartAdbAsync()
        {
            try { await RunAdbAsync("kill-server", 4000); } catch { }
            await Task.Delay(350);
            try { await RunAdbAsync("start-server", 12000); } catch { }
            try { await RunAdbAsync("wait-for-device", 3500); } catch { }
        }

        public async Task<DeviceStatus> PollStatusAsync(bool allowSelfHeal)
        {
            if (Interlocked.CompareExchange(ref _isPolling, 1, 0) != 0)
                return _lastStatus;

            try
            {
                string output = await RunAdbAsync("devices -l", 5000);
                var lines = new List<string>();
                foreach (var raw in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string line = raw.Trim();
                    if (!line.StartsWith("List of devices") && !line.StartsWith("*"))
                        lines.Add(line);
                }

                if (lines.Count == 0)
                {
                    _consecutiveDisconnected++;
                    _cachedSerial = null;
                    bool hasUsb = CheckWindowsUsbPresent();

                    if (hasUsb && allowSelfHeal && _consecutiveDisconnected >= 3 && (DateTime.UtcNow - _lastHealAt).TotalSeconds > 15)
                    {
                        _lastHealAt = DateTime.UtcNow;
                        _consecutiveDisconnected = 0;
                        try { await RunAdbAsync("reconnect", 3500); } catch { }
                        await RestartAdbAsync();
                    }

                    _lastStatus = new DeviceStatus
                    {
                        AdbState = "disconnected",
                        UsbDetected = hasUsb
                    };
                    return _lastStatus;
                }

                _consecutiveDisconnected = 0;
                var parts = Regex.Split(lines[0], @"\s+");
                string serial = parts.Length > 0 ? parts[0] : null;
                string state = parts.Length > 1 ? parts[1] : "offline";

                var st = new DeviceStatus
                {
                    Serial = serial,
                    AdbState = state,
                    UsbDetected = true,
                    Model = _lastStatus.Model,
                    MarketName = _lastStatus.MarketName,
                    HyperOsVersion = _lastStatus.HyperOsVersion,
                    AndroidVersion = _lastStatus.AndroidVersion,
                    Resolution = _lastStatus.Resolution,
                    PhysicalWidth = _lastStatus.PhysicalWidth,
                    PhysicalHeight = _lastStatus.PhysicalHeight,
                    BatteryLevel = _lastStatus.BatteryLevel
                };

                if (state == "offline" && (DateTime.UtcNow - _lastHealAt).TotalSeconds > 8)
                {
                    _lastHealAt = DateTime.UtcNow;
                    try { await RunAdbAsync("reconnect offline", 3000); } catch { }
                }

                if (state == "device" && !string.IsNullOrEmpty(serial))
                {
                    if (_cachedSerial != serial)
                    {
                        st.MarketName = (await SafeShellProp(serial, "ro.product.marketname"))?.Trim();
                        st.Model = (await SafeShellProp(serial, "ro.product.model"))?.Trim();
                        if (string.IsNullOrEmpty(st.MarketName)) st.MarketName = st.Model ?? "Android Device";
                        st.HyperOsVersion = (await SafeShellProp(serial, "ro.mi.os.version.showname"))?.Trim();
                        if (string.IsNullOrEmpty(st.HyperOsVersion)) st.HyperOsVersion = "Android";
                        st.AndroidVersion = (await SafeShellProp(serial, "ro.build.version.release"))?.Trim() ?? "--";

                        try
                        {
                            string wm = await RunAdbAsync($"-s {serial} shell wm size", 3000);
                            var m = Regex.Match(wm, @"Override size:\s*(\d+)x(\d+)");
                            if (!m.Success) m = Regex.Match(wm, @"Physical size:\s*(\d+)x(\d+)");
                            if (!m.Success) m = Regex.Match(wm, @"(\d+)x(\d+)");
                            int pw, ph;
                            if (m.Success && int.TryParse(m.Groups[1].Value, out pw) && int.TryParse(m.Groups[2].Value, out ph) && pw > 0 && ph > 0)
                            {
                                st.PhysicalWidth = pw;
                                st.PhysicalHeight = ph;
                                st.Resolution = $"{pw}x{ph}";
                            }
                            else
                            {
                                st.PhysicalWidth = 1200;
                                st.PhysicalHeight = 2670;
                                st.Resolution = "1200x2670";
                            }
                        }
                        catch
                        {
                            st.PhysicalWidth = 1200;
                            st.PhysicalHeight = 2670;
                            st.Resolution = "1200x2670";
                        }
                        _cachedSerial = serial;
                    }

                    if ((DateTime.UtcNow - _lastBatteryAt).TotalSeconds > 30 || !st.BatteryLevel.HasValue)
                    {
                        _lastBatteryAt = DateTime.UtcNow;
                        try
                        {
                            string bat = await RunAdbAsync($"-s {serial} shell dumpsys battery", 3000);
                            var m = Regex.Match(bat, @"level:\s*(\d+)");
                            if (m.Success) st.BatteryLevel = int.Parse(m.Groups[1].Value);
                        }
                        catch { }
                    }
                }

                _lastStatus = st;
                return st;
            }
            catch
            {
                return _lastStatus;
            }
            finally
            {
                Interlocked.Exchange(ref _isPolling, 0);
            }
        }

        private async Task<string> SafeShellProp(string serial, string prop)
        {
            try
            {
                return await RunAdbAsync($"-s {serial} shell getprop {prop}", 3000);
            }
            catch
            {
                return "";
            }
        }

        private bool CheckWindowsUsbPresent()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -Command \"@(Get-PnpDevice -PresentOnly | Where-Object { $_.InstanceId -match 'VID_2717|VID_18D1|VID_04E8|VID_12D1|VID_22D9|VID_2A70|VID_2D95|VID_22B8|VID_0FCE' }).Count\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    string s = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(3000);
                    int count;
                    return int.TryParse(s, out count) && count > 0;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
