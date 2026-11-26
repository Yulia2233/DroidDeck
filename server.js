const http = require('http');
const net = require('net');
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { spawn, execFile } = require('child_process');

const PORT = Number(process.env.PORT || 3080);
const SCRCPY_DIR = path.join(__dirname, 'bin', 'scrcpy');
const ADB_PATH = path.join(SCRCPY_DIR, 'adb.exe');
const SCRCPY_EXE = path.join(SCRCPY_DIR, 'scrcpy.exe');
const SCRCPY_SERVER_JAR = path.join(SCRCPY_DIR, 'scrcpy-server');
const PUBLIC_DIR = path.join(__dirname, 'public');
const KEYMAPS_FILE = path.join(__dirname, 'keymaps.json');

// Force libusb backend + disable burst mode for ADB on Windows 11 + USB Hubs
// to eliminate AdbWinApi drops, "failed to get feature set", and LIBUSB_ERROR_PIPE stalls.
const ADB_ENV = {
  ...process.env,
  ADB_LIBUSB: '1',
  ADB_BURST_MODE: '0',
  ADB_MDNS_AUTO_CONNECT: '0'
};

// ============================================================================
// Minimal zero-dependency RFC 6455 WebSocket Server implementation
// Supports text (JSON) and binary (H.264 NAL frames) messages at 120 FPS
// ============================================================================
class SimpleWebSocket {
  constructor(socket) {
    this.socket = socket;
    this.isAlive = true;
    this.buffer = Buffer.alloc(0);
    this.onMessage = null;
    this.onClose = null;

    this.socket.setNoDelay(true);
    this.socket.on('data', (chunk) => this._handleData(chunk));
    this.socket.on('close', () => {
      this.isAlive = false;
      if (this.onClose) this.onClose();
    });
    this.socket.on('error', () => {
      this.isAlive = false;
    });
  }

  _handleData(chunk) {
    this.buffer = Buffer.concat([this.buffer, chunk]);
    while (this.buffer.length >= 2) {
      const firstByte = this.buffer[0];
      const secondByte = this.buffer[1];
      const opcode = firstByte & 0x0f;
      const isMasked = (secondByte & 0x80) !== 0;
      let payloadLen = secondByte & 0x7f;
      let offset = 2;

      if (payloadLen === 126) {
        if (this.buffer.length < 4) return;
        payloadLen = this.buffer.readUInt16BE(2);
        offset = 4;
      } else if (payloadLen === 127) {
        if (this.buffer.length < 10) return;
        payloadLen = Number(this.buffer.readBigUInt64BE(2));
        offset = 10;
      }

      const maskLen = isMasked ? 4 : 0;
      if (this.buffer.length < offset + maskLen + payloadLen) return;

      let payload = this.buffer.subarray(offset + maskLen, offset + maskLen + payloadLen);
      if (isMasked) {
        const mask = this.buffer.subarray(offset, offset + 4);
        const unmasked = Buffer.allocUnsafe(payload.length);
        for (let i = 0; i < payload.length; i++) {
          unmasked[i] = payload[i] ^ mask[i & 3];
        }
        payload = unmasked;
      }

      this.buffer = this.buffer.subarray(offset + maskLen + payloadLen);

      if (opcode === 0x08) {
        // Close frame
        this.close();
        return;
      } else if (opcode === 0x09) {
        // Ping -> Pong
        this._sendFrame(0x0a, payload);
      } else if (opcode === 0x01) {
        // Text frame
        if (this.onMessage) {
          this.onMessage(payload.toString('utf8'), false);
        }
      } else if (opcode === 0x02) {
        // Binary frame
        if (this.onMessage) {
          this.onMessage(payload, true);
        }
      }
    }
  }

  sendJson(obj) {
    if (!this.isAlive) return;
    const data = Buffer.from(JSON.stringify(obj), 'utf8');
    this._sendFrame(0x01, data);
  }

  sendBinary(buf, isDroppable = false) {
    if (!this.isAlive || this.socket.destroyed) return;
    // Drop non-critical delta frames if browser socket backpressures > 2MB
    if (isDroppable && this.socket.writableLength > 2 * 1024 * 1024) {
      return;
    }
    this._sendFrame(0x02, buf);
  }

  _sendFrame(opcode, payload) {
    if (!this.isAlive || this.socket.destroyed) return;
    const len = payload.length;
    let header;
    if (len < 126) {
      header = Buffer.allocUnsafe(2);
      header[0] = 0x80 | opcode;
      header[1] = len;
    } else if (len < 65536) {
      header = Buffer.allocUnsafe(4);
      header[0] = 0x80 | opcode;
      header[1] = 126;
      header.writeUInt16BE(len, 2);
    } else {
      header = Buffer.allocUnsafe(10);
      header[0] = 0x80 | opcode;
      header[1] = 127;
      header.writeBigUInt64BE(BigInt(len), 2);
    }
    try {
      this.socket.write(Buffer.concat([header, payload]));
    } catch (_) {
      this.isAlive = false;
    }
  }

  close() {
    this.isAlive = false;
    try {
      this.socket.end();
    } catch (_) {}
  }
}

// ============================================================================
// Helper: Run ADB command (with ADB_LIBUSB=1)
// ============================================================================
function runAdb(args, timeout = 8000, encoding = 'utf8') {
  return new Promise((resolve, reject) => {
    execFile(
      ADB_PATH,
      args,
      {
        timeout,
        maxBuffer: 25 * 1024 * 1024,
        encoding: encoding === 'buffer' ? 'buffer' : 'utf8',
        env: ADB_ENV
      },
      (err, stdout, stderr) => {
        if (err) {
          return reject(new Error(stderr ? stderr.toString().trim() || err.message : err.message));
        }
        resolve(stdout);
      }
    );
  });
}

async function restartAdbWithLibusb() {
  await runAdb(['kill-server'], 4000).catch(() => {});
  await new Promise((r) => setTimeout(r, 350));
  await runAdb(['start-server'], 12000).catch(() => {});
  // ADB_LIBUSB takes ~2s for initial USB bus scan on Windows
  await runAdb(['wait-for-device'], 3500).catch(() => {});
}

async function ensureAdbReady() {
  try {
    const features = await runAdb(['host-features'], 2500);
    if (features.includes('libusb')) {
      return;
    }
  } catch (_) {}
  await restartAdbWithLibusb();
}

function checkWindowsUsbXiaomi() {
  return new Promise((resolve) => {
    execFile(
      'powershell.exe',
      [
        '-NoProfile',
        '-Command',
        "Get-PnpDevice -PresentOnly | Where-Object { $_.InstanceId -match 'VID_2717|VID_18D1' -or $_.FriendlyName -match 'Xiaomi|Mi |Redmi|ADB|Android|MTP' } | Select-Object Status,Class,FriendlyName,InstanceId | ConvertTo-Json -Compress"
      ],
      { timeout: 5000 },
      (err, stdout) => {
        if (err || !stdout || !stdout.trim()) return resolve([]);
        try {
          const parsed = JSON.parse(stdout.trim());
          resolve(Array.isArray(parsed) ? parsed : [parsed]);
        } catch (_) {
          resolve([]);
        }
      }
    );
  });
}

// ============================================================================
// Device Discovery & Detailed Telemetry for Xiaomi 15
// ============================================================================
let currentDeviceState = {
  usbDetected: false,
  usbDevices: [],
  adbState: 'disconnected', // 'disconnected' | 'unauthorized' | 'offline' | 'device'
  serial: null,
  model: null,
  marketName: null,
  hyperOsVersion: null,
  androidVersion: null,
  resolution: null,
  batteryLevel: null,
  usbSecurityEnabled: null,
  streamActive: false,
  nativeScrcpyActive: false
};

let isPolling = false;
let cachedPropsSerial = null;
let lastBatteryCheckAt = 0;
let lastAdbHealAt = 0;
let consecutiveDisconnected = 0;

async function pollDeviceStatus() {
  if (isPolling) return currentDeviceState;
  isPolling = true;
  try {
    const out = await runAdb(['devices', '-l'], 6000);
    const lines = out
      .split(/\r?\n/)
      .map((l) => l.trim())
      .filter((l) => l && !l.startsWith('List of devices') && !l.startsWith('*'));

    if (lines.length === 0) {
      consecutiveDisconnected++;
      const usbList = await checkWindowsUsbXiaomi();
      const hasAdbUsb = usbList.some((d) => String(d.InstanceId || '').includes('VID_18D1'));

      // Self-heal only after 3 consecutive empty polls (~6s) and never while stream is active
      const now = Date.now();
      if (hasAdbUsb && !scrcpySession.active && consecutiveDisconnected >= 3 && now - lastAdbHealAt > 15000) {
        lastAdbHealAt = now;
        consecutiveDisconnected = 0;
        console.log('[adb-heal] 检测到 USB ADB 接口在线但 adb 未列出设备，自动恢复 libusb 连接...');
        await runAdb(['reconnect'], 4000).catch(() => {});
        const retryOut = await runAdb(['devices', '-l'], 5000).catch(() => '');
        const retryLines = retryOut
          .split(/\r?\n/)
          .map((l) => l.trim())
          .filter((l) => l && !l.startsWith('List of devices') && !l.startsWith('*'));
        if (retryLines.length === 0) {
          await restartAdbWithLibusb();
        }
      }

      cachedPropsSerial = null;
      currentDeviceState = {
        ...currentDeviceState,
        usbDetected: usbList.length > 0,
        usbDevices: usbList,
        adbState: 'disconnected',
        serial: null
      };
      return currentDeviceState;
    }

    consecutiveDisconnected = 0;

    // Parse first attached device
    const first = lines[0];
    const parts = first.split(/\s+/);
    const serial = parts[0];
    const status = parts[1]; // 'device', 'unauthorized', 'offline'

    currentDeviceState.serial = serial;
    currentDeviceState.adbState = status;
    currentDeviceState.usbDetected = true;

    if (status === 'offline' && Date.now() - lastAdbHealAt > 8000) {
      lastAdbHealAt = Date.now();
      runAdb(['reconnect', 'offline'], 4000).catch(() => {});
    }

    if (status === 'device') {
      // Fetch static Xiaomi 15 properties ONLY once per connection to avoid hammering USB/adbd
      if (cachedPropsSerial !== serial) {
        const [marketName, model, hyperOs, androidVer, wmSize] = await Promise.all([
          runAdb(['-s', serial, 'shell', 'getprop', 'ro.product.marketname']).catch(() => ''),
          runAdb(['-s', serial, 'shell', 'getprop', 'ro.product.model']).catch(() => ''),
          runAdb(['-s', serial, 'shell', 'getprop', 'ro.mi.os.version.showname']).catch(() => ''),
          runAdb(['-s', serial, 'shell', 'getprop', 'ro.build.version.release']).catch(() => ''),
          runAdb(['-s', serial, 'shell', 'wm', 'size']).catch(() => '')
        ]);

        currentDeviceState.marketName = marketName.trim() || model.trim() || 'Xiaomi Device';
        currentDeviceState.model = model.trim() || serial;
        currentDeviceState.hyperOsVersion = hyperOs.trim() || 'HyperOS';
        currentDeviceState.androidVersion = androidVer.trim() || '15';

        const sizeMatch = wmSize.match(/(\d+x\d+)/);
        currentDeviceState.resolution = sizeMatch ? sizeMatch[1] : '1200x2670';
        cachedPropsSerial = serial;
      }

      // Refresh battery at most once every 30s (never every 2s during active game streaming)
      const now = Date.now();
      if (now - lastBatteryCheckAt > 30000) {
        lastBatteryCheckAt = now;
        const batteryOut = await runAdb(['-s', serial, 'shell', 'dumpsys', 'battery']).catch(() => '');
        const levelMatch = batteryOut.match(/level:\s*(\d+)/);
        if (levelMatch) currentDeviceState.batteryLevel = Number(levelMatch[1]);
      }
    }
  } catch (_) {
  } finally {
    isPolling = false;
  }
  return currentDeviceState;
}

// ============================================================================
// Scrcpy Direct Streaming & Multi-Touch Control Session
// Streams low-latency H.264 NAL units + injects 32-byte binary multi-touch events
// ============================================================================
class ScrcpyDirectSession {
  constructor() {
    this.active = false;
    this.wantActive = false;
    this.isStarting = false;
    this.isReconnecting = false;
    this.reconnectTimer = null;
    this.serial = null;
    this.port = 27189;
    this.scid = '15a02026';
    this.serverProc = null;
    this.videoSocket = null;
    this.controlSocket = null;
    this.deviceName = 'Xiaomi 15';
    this.width = 1200;
    this.height = 2670;
    this.spsPpsConfig = null; // Cached H.264 SPS/PPS config packet
    this.lastKeyframe = null; // Cached latest IDR keyframe for instant client join
    this.options = {
      maxSize: 1280,
      bitRate: 12000000,
      maxFps: 60
    };
  }

  async start(serial, opts = {}) {
    if (this.isStarting) return;
    this.isStarting = true;
    this.wantActive = true;
    this.isReconnecting = false;
    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = null;
    }

    try {
      await this.stop(false);
      this.serial = serial;
      this.options = {
        maxSize: Number(opts.maxSize) || 1280,
        bitRate: Number(opts.bitRate) || 12000000,
        maxFps: Number(opts.maxFps) || 60
      };

      const scidNum = Math.floor(Math.random() * 0x7fffffff);
      this.scid = scidNum.toString(16).padStart(8, '0');
      this.port = 27180 + Math.floor(Math.random() * 50);

      // 1. Push scrcpy-server & forward port with auto-retry on transient "failed to get feature set"
      let prepErr = null;
      for (let attempt = 1; attempt <= 3; attempt++) {
        try {
          await runAdb(['-s', serial, 'push', SCRCPY_SERVER_JAR, '/data/local/tmp/scrcpy-server.jar'], 10000);
          await runAdb(['-s', serial, 'forward', `tcp:${this.port}`, `localabstract:scrcpy_${this.scid}`], 5000);
          prepErr = null;
          break;
        } catch (err) {
          prepErr = err;
          console.warn(`[scrcpy-prep] 第 ${attempt} 次尝试失败: ${err.message}`);
          if (attempt < 3) {
            await runAdb(['reconnect', 'offline'], 3000).catch(() => {});
            await new Promise((r) => setTimeout(r, 700));
            await runAdb(['wait-for-device'], 5000).catch(() => {});
          }
        }
      }
      if (prepErr) throw prepErr;

      // 3. Spawn scrcpy-server on device (1s keyframe interval for fast recovery)
      const serverArgs = [
        '-s',
        serial,
        'shell',
        'CLASSPATH=/data/local/tmp/scrcpy-server.jar',
        'app_process',
        '/',
        'com.genymobile.scrcpy.Server',
        '5.0.1',
        `scid=${this.scid}`,
        'log_level=info',
        'tunnel_forward=true',
        'audio=false',
        'video=true',
        'control=true',
        'video_codec=h264',
        'video_codec_options=i-frame-interval=1',
        `max_size=${this.options.maxSize}`,
        `video_bit_rate=${this.options.bitRate}`,
        `max_fps=${this.options.maxFps}`,
        'stay_awake=true',
        'power_on=true',
        'clipboard_autosync=false',
        'cleanup=false'
      ];

      const proc = spawn(ADB_PATH, serverArgs, { stdio: ['ignore', 'pipe', 'pipe'], env: ADB_ENV });
      this.serverProc = proc;
      proc.stdout.on('data', (d) => {
        const msg = d.toString().trim();
        if (msg) console.log('[scrcpy-server]', msg);
      });
      proc.stderr.on('data', (d) => {
        const msg = d.toString().trim();
        if (msg) console.log('[scrcpy-server:err]', msg);
      });
      proc.on('exit', () => {
        if (this.serverProc === proc && this.active) {
          this._handleUnexpectedDisconnect('server_exit');
        }
      });

      // 4. Connect Video & Control sockets with retry while server boots
      await this._connectSockets();
      this.active = true;
      currentDeviceState.streamActive = true;

      broadcastJson({
        type: 'stream_status',
        active: true,
        width: this.width,
        height: this.height,
        deviceName: this.deviceName,
        options: this.options
      });
    } finally {
      this.isStarting = false;
    }
  }

  _handleUnexpectedDisconnect(reason) {
    if (!this.wantActive || this.isReconnecting) return;
    this.isReconnecting = true;
    console.warn(`[scrcpy] 连接中断 (${reason})，将在手机恢复后自动重连...`);
    this.stop(false).then(() => {
      if (!this.wantActive) {
        this.isReconnecting = false;
        return;
      }
      broadcastJson({ type: 'stream_reconnecting', reason });
      this._scheduleReconnect(0);
    });
  }

  _scheduleReconnect(attempt) {
    if (!this.wantActive) {
      this.isReconnecting = false;
      return;
    }
    if (attempt >= 25) {
      this.wantActive = false;
      this.isReconnecting = false;
      broadcastJson({ type: 'stream_status', active: false, reason: 'reconnect_timeout' });
      return;
    }

    this.reconnectTimer = setTimeout(async () => {
      if (!this.wantActive) return;
      const st = await pollDeviceStatus();
      if (st.adbState === 'device' && st.serial) {
        try {
          await this.start(st.serial, this.options);
          return;
        } catch (err) {
          console.warn('[scrcpy-reconnect] 重连失败:', err.message);
        }
      }
      this._scheduleReconnect(attempt + 1);
    }, 1200);
  }

  _connectSockets() {
    return new Promise((resolve, reject) => {
      let attempts = 0;
      const maxAttempts = 35;

      const tryConnectVideo = () => {
        attempts++;
        const vSock = net.createConnection({ host: '127.0.0.1', port: this.port });
        vSock.setNoDelay(true);

        let gotDummy = false;
        let state = 'DUMMY'; // DUMMY -> HEADER -> FRAMES
        let buf = Buffer.alloc(0);

        const onTempError = () => {
          vSock.destroy();
          if (attempts < maxAttempts) {
            setTimeout(tryConnectVideo, 120);
          } else {
            reject(new Error('无法连接到手机投屏服务端口，请检查 USB 调试连接'));
          }
        };

        vSock.once('error', onTempError);

        vSock.on('data', (chunk) => {
          buf = Buffer.concat([buf, chunk]);

          if (state === 'DUMMY') {
            if (buf.length >= 1) {
              gotDummy = true;
              vSock.removeListener('error', onTempError);
              buf = buf.subarray(1); // consume 1 dummy byte

              // Connect second socket: Control Socket!
              const cSock = net.createConnection({ host: '127.0.0.1', port: this.port });
              cSock.setNoDelay(true);
              cSock.on('data', () => {}); // Drain device messages (clipboard/UHID acks) so TCP never stalls
              cSock.on('error', () => {});
              this.controlSocket = cSock;
              this.videoSocket = vSock;

              state = 'HEADER';
            }
          }

          if (state === 'HEADER') {
            // In scrcpy 5.0.1: 64B device name + 4B codec_id + 4B flags (0x80000000) + 4B width + 4B height = 80 bytes
            if (buf.length >= 80) {
              const nameBuf = buf.subarray(0, 64);
              const nullIdx = nameBuf.indexOf(0);
              this.deviceName = nameBuf.subarray(0, nullIdx >= 0 ? nullIdx : 64).toString('utf8') || 'Xiaomi 15';
              this.width = buf.readUInt32BE(72);
              this.height = buf.readUInt32BE(76);
              buf = buf.subarray(80);
              state = 'FRAMES';
              resolve();
            }
          }

          if (state === 'FRAMES') {
            while (buf.length >= 12) {
              const ptsFlags = buf.readBigUInt64BE(0);
              const packetSize = buf.readUInt32BE(8);
              if (buf.length < 12 + packetSize) break;

              const h264Payload = buf.subarray(12, 12 + packetSize);
              buf = buf.subarray(12 + packetSize);

              // In scrcpy 5.0.1: bit 62 (0x4000000000000000) = CONFIG, bit 61 (0x2000000000000000) = KEY_FRAME
              const isConfig = ((ptsFlags >> 62n) & 1n) === 1n || ((ptsFlags >> 63n) & 1n) === 1n;
              const isKeyframe = !isConfig && (((ptsFlags >> 61n) & 1n) === 1n);

              // Check if SPS indicates resolution change (e.g. phone rotated to landscape for gaming!)
              if (isConfig) {
                this.spsPpsConfig = Buffer.from(h264Payload);
                const dims = parseH264SpsDimensions(this.spsPpsConfig);
                if (dims && dims.width > 0 && dims.height > 0 && dims.width <= 65535 && dims.height <= 65535) {
                  if (dims.width !== this.width || dims.height !== this.height) {
                    this.width = dims.width;
                    this.height = dims.height;
                    broadcastJson({
                      type: 'stream_meta',
                      width: this.width,
                      height: this.height,
                      deviceName: this.deviceName
                    });
                  }
                }
              }

              const safeW = Math.min(65535, Math.max(1, this.width || 1200));
              const safeH = Math.min(65535, Math.max(1, this.height || 2670));

              // Build compact binary packet for browser WebCodecs:
              // [1B flags (bit0=config, bit1=keyframe)] [2B width] [2B height] [H264 Annex-B bytes]
              const outPacket = Buffer.allocUnsafe(5 + h264Payload.length);
              outPacket[0] = (isConfig ? 1 : 0) | (isKeyframe ? 2 : 0);
              outPacket.writeUInt16BE(safeW, 1);
              outPacket.writeUInt16BE(safeH, 3);
              h264Payload.copy(outPacket, 5);

              if (isKeyframe && this.spsPpsConfig) {
                this.lastKeyframe = Buffer.from(outPacket);
              }

              broadcastBinary(outPacket, !isConfig && !isKeyframe);
            }
          }
        });

        vSock.on('close', () => {
          if (!gotDummy && attempts < maxAttempts) {
            setTimeout(tryConnectVideo, 120);
          } else if (gotDummy && this.videoSocket === vSock && this.active) {
            this._handleUnexpectedDisconnect('video_socket_closed');
          }
        });
      };

      setTimeout(tryConnectVideo, 250);
    });
  }

  /**
   * Inject low-latency multi-touch event (<2ms) via scrcpy control socket
   * action: 0 = DOWN, 1 = UP, 2 = MOVE
   * pointerId: integer (0..20) allowing simultaneous WASD joystick + Mouse aim + Skill buttons
   */
  injectTouch(action, pointerId, x, y, clientWidth, clientHeight) {
    const w = clientWidth || this.width || 1200;
    const h = clientHeight || this.height || 2670;
    const clampedX = Math.max(0, Math.min(w - 1, Math.round(x)));
    const clampedY = Math.max(0, Math.min(h - 1, Math.round(y)));

    if (this.controlSocket && !this.controlSocket.destroyed) {
      // Scrcpy TYPE_INJECT_TOUCH_EVENT = 2 (32 bytes)
      const msg = Buffer.allocUnsafe(32);
      msg.writeUInt8(2, 0); // type = 2
      msg.writeUInt8(action, 1); // 0=DOWN, 1=UP, 2=MOVE
      msg.writeBigInt64BE(BigInt(pointerId), 2);
      msg.writeInt32BE(clampedX, 10);
      msg.writeInt32BE(clampedY, 14);
      msg.writeUInt16BE(w, 18);
      msg.writeUInt16BE(h, 20);
      msg.writeUInt16BE(action === 1 ? 0x0000 : 0xffff, 22); // pressure
      msg.writeInt32BE(1, 24); // actionButton = AMOTION_EVENT_BUTTON_PRIMARY
      msg.writeInt32BE(action === 1 ? 0 : 1, 28); // buttons
      try {
        this.controlSocket.write(msg);
        return true;
      } catch (_) {}
    }

    // Fallback to adb input tap if control socket isn't active
    if (action === 0 && this.serial) {
      runAdb(['-s', this.serial, 'shell', 'input', 'tap', String(clampedX), String(clampedY)]).catch(() => {});
    }
    return false;
  }

  injectKeycode(action, keycode, metaState = 0) {
    if (this.controlSocket && !this.controlSocket.destroyed) {
      // Scrcpy TYPE_INJECT_KEYCODE = 0 (14 bytes)
      const msg = Buffer.allocUnsafe(14);
      msg.writeUInt8(0, 0);
      msg.writeUInt8(action, 1); // 0=DOWN, 1=UP
      msg.writeInt32BE(keycode, 2);
      msg.writeInt32BE(0, 6); // repeat
      msg.writeInt32BE(metaState, 10);
      try {
        this.controlSocket.write(msg);
        return true;
      } catch (_) {}
    }
    if (action === 0 && this.serial) {
      runAdb(['-s', this.serial, 'shell', 'input', 'keyevent', String(keycode)]).catch(() => {});
    }
    return false;
  }

  injectScroll(x, y, clientWidth, clientHeight, hScroll, vScroll) {
    if (this.controlSocket && !this.controlSocket.destroyed) {
      const w = clientWidth || this.width || 1200;
      const h = clientHeight || this.height || 2670;
      const clampedX = Math.max(0, Math.min(w - 1, Math.round(x)));
      const clampedY = Math.max(0, Math.min(h - 1, Math.round(y)));
      // Scrcpy 2.x/3.x/5.x TYPE_INJECT_SCROLL_EVENT = 3 (21 bytes)
      const msg = Buffer.allocUnsafe(21);
      msg.writeUInt8(3, 0);
      msg.writeInt32BE(clampedX, 1);
      msg.writeInt32BE(clampedY, 5);
      msg.writeUInt16BE(w, 9);
      msg.writeUInt16BE(h, 11);
      msg.writeInt16BE(Math.max(-32768, Math.min(32767, Math.round(hScroll * 2048))), 13);
      msg.writeInt16BE(Math.max(-32768, Math.min(32767, Math.round(vScroll * 2048))), 15);
      msg.writeInt32BE(0, 17);
      try {
        this.controlSocket.write(msg);
        return true;
      } catch (_) {}
    }
    return false;
  }

  setScreenPowerMode(turnOn) {
    if (this.controlSocket && !this.controlSocket.destroyed) {
      // Scrcpy TYPE_SET_DISPLAY_POWER = 10 (2 bytes)
      const msg = Buffer.allocUnsafe(2);
      msg.writeUInt8(10, 0);
      msg.writeUInt8(turnOn ? 1 : 0, 1);
      try {
        this.controlSocket.write(msg);
        return true;
      } catch (_) {}
    }
    return false;
  }

  async stop(userRequested = true) {
    if (userRequested) {
      this.wantActive = false;
      this.isReconnecting = false;
      if (this.reconnectTimer) {
        clearTimeout(this.reconnectTimer);
        this.reconnectTimer = null;
      }
    }
    this.active = false;
    currentDeviceState.streamActive = false;
    this.spsPpsConfig = null;
    this.lastKeyframe = null;

    if (this.videoSocket) {
      const vs = this.videoSocket;
      this.videoSocket = null;
      try {
        vs.destroy();
      } catch (_) {}
    }
    if (this.controlSocket) {
      const cs = this.controlSocket;
      this.controlSocket = null;
      try {
        cs.destroy();
      } catch (_) {}
    }
    if (this.serverProc) {
      const sp = this.serverProc;
      this.serverProc = null;
      try {
        sp.kill();
      } catch (_) {}
    }
    if (this.serial && this.port) {
      await runAdb(['-s', this.serial, 'forward', '--remove', `tcp:${this.port}`], 2000).catch(() => {});
    }
  }
}

// ============================================================================
// Lightweight H.264 SPS Parser (detects dynamic portrait <-> landscape rotation)
// ============================================================================
function parseH264SpsDimensions(annexB) {
  try {
    // Find NAL unit type 7 (SPS)
    let i = 0;
    while (i < annexB.length - 4) {
      let startOffset = -1;
      if (annexB[i] === 0 && annexB[i + 1] === 0 && annexB[i + 2] === 1) {
        startOffset = i + 3;
      } else if (annexB[i] === 0 && annexB[i + 1] === 0 && annexB[i + 2] === 0 && annexB[i + 3] === 1) {
        startOffset = i + 4;
      }
      if (startOffset !== -1) {
        const nalType = annexB[startOffset] & 0x1f;
        if (nalType === 7) {
          // Find end of SPS NAL
          let end = annexB.length;
          for (let j = startOffset + 1; j < annexB.length - 3; j++) {
            if (
              (annexB[j] === 0 && annexB[j + 1] === 0 && annexB[j + 2] === 1) ||
              (annexB[j] === 0 && annexB[j + 1] === 0 && annexB[j + 2] === 0 && annexB[j + 3] === 1)
            ) {
              end = j;
              break;
            }
          }
          return decodeSpsNal(annexB.subarray(startOffset + 1, end));
        }
        i = startOffset;
      } else {
        i++;
      }
    }
  } catch (_) {}
  return null;
}

function decodeSpsNal(nalPayload) {
  // Remove emulation prevention bytes (0x00 0x00 0x03 -> 0x00 0x00)
  const rbsp = [];
  for (let i = 0; i < nalPayload.length; i++) {
    if (i >= 2 && nalPayload[i] === 0x03 && rbsp[rbsp.length - 1] === 0x00 && rbsp[rbsp.length - 2] === 0x00) {
      continue;
    }
    rbsp.push(nalPayload[i]);
  }
  let bitPos = 0;
  const readBit = () => {
    const byteIdx = bitPos >> 3;
    if (byteIdx >= rbsp.length) return 0;
    const bitIdx = 7 - (bitPos & 7);
    bitPos++;
    return (rbsp[byteIdx] >> bitIdx) & 1;
  };
  const readBits = (n) => {
    let val = 0;
    for (let i = 0; i < n; i++) val = (val << 1) | readBit();
    return val;
  };
  const readUE = () => {
    let zeros = 0;
    while (readBit() === 0 && zeros < 32) zeros++;
    return (1 << zeros) - 1 + readBits(zeros);
  };
  const readSE = () => {
    const u = readUE();
    return u & 1 ? (u + 1) >> 1 : -(u >> 1);
  };

  const profileIdc = readBits(8);
  readBits(8); // constraints
  readBits(8); // level_idc
  readUE(); // seq_parameter_set_id

  let chromaFormatIdc = 1;
  if ([100, 110, 122, 244, 44, 83, 86, 118, 128, 138, 139, 134, 135].includes(profileIdc)) {
    chromaFormatIdc = readUE();
    if (chromaFormatIdc === 3) readBit();
    readUE();
    readUE();
    readBit();
    const seqScalingMatrixPresent = readBit();
    if (seqScalingMatrixPresent) {
      const count = chromaFormatIdc !== 3 ? 8 : 12;
      for (let i = 0; i < count; i++) {
        if (readBit()) {
          const size = i < 6 ? 16 : 64;
          let lastScale = 8;
          let nextScale = 8;
          for (let j = 0; j < size; j++) {
            if (nextScale !== 0) {
              nextScale = (lastScale + readSE() + 256) % 256;
            }
            lastScale = nextScale === 0 ? lastScale : nextScale;
          }
        }
      }
    }
  }

  readUE(); // log2_max_frame_num_minus4
  const picOrderCntType = readUE();
  if (picOrderCntType === 0) {
    readUE();
  } else if (picOrderCntType === 1) {
    readBit();
    readSE();
    readSE();
    const numRef = readUE();
    for (let i = 0; i < numRef; i++) readSE();
  }
  readUE(); // max_num_ref_frames
  readBit(); // gaps_in_frame_num_value_allowed_flag
  const picWidthInMbsMinus1 = readUE();
  const picHeightInMapUnitsMinus1 = readUE();
  const frameMbsOnlyFlag = readBit();
  if (!frameMbsOnlyFlag) readBit();
  readBit(); // direct_8x8_inference_flag
  const frameCroppingFlag = readBit();
  let cropLeft = 0,
    cropRight = 0,
    cropTop = 0,
    cropBottom = 0;
  if (frameCroppingFlag) {
    cropLeft = readUE();
    cropRight = readUE();
    cropTop = readUE();
    cropBottom = readUE();
  }

  const width = (picWidthInMbsMinus1 + 1) * 16 - (cropLeft + cropRight) * 2;
  const height = (2 - frameMbsOnlyFlag) * (picHeightInMapUnitsMinus1 + 1) * 16 - (cropTop + cropBottom) * 2;
  return { width, height };
}

// ============================================================================
// Native Scrcpy 120FPS Window Launcher
// ============================================================================
let nativeScrcpyProc = null;
let isLaunchingNative = false;

async function launchNativeScrcpy(serial, config = {}, retryCount = 0) {
  if (isLaunchingNative) return;
  isLaunchingNative = true;
  try {
    if (scrcpySession.wantActive || scrcpySession.active) {
      await scrcpySession.stop(true);
      broadcastJson({ type: 'stream_status', active: false, reason: 'switched_to_native' });
    }

    if (nativeScrcpyProc) {
      try {
        nativeScrcpyProc.kill();
      } catch (_) {}
      nativeScrcpyProc = null;
      await new Promise((r) => setTimeout(r, 250));
    }

    const args = [];
    if (serial) args.push('-s', serial);
    args.push('--window-title=DroidDeck-120FPS');

    const maxSize = Number(config.maxSize || 0);
    if (maxSize > 0) args.push(`--max-size=${maxSize}`);

    const maxFps = Number(config.maxFps || 120);
    if (maxFps > 0) args.push(`--max-fps=${maxFps}`);

    const bitRateM = Number(config.bitRateM || 20);
    if (bitRateM > 0) args.push(`--video-bit-rate=${bitRateM}M`);

    if (config.turnScreenOff) args.push('--turn-screen-off');
    if (config.stayAwake !== false) args.push('--stay-awake');
    if (config.alwaysOnTop) args.push('--always-on-top');
    if (config.fullscreen) args.push('--fullscreen');
    if (config.noAudio) args.push('--no-audio');
    if (config.otgMode) args.push('--otg');

    const proc = spawn(SCRCPY_EXE, args, {
      cwd: SCRCPY_DIR,
      stdio: ['ignore', 'pipe', 'pipe'],
      env: ADB_ENV
    });
    nativeScrcpyProc = proc;

    let errLogs = '';
    proc.stdout.on('data', (d) => {
      const s = d.toString().trim();
      if (s) console.log('[scrcpy-native]', s);
    });
    proc.stderr.on('data', (d) => {
      const s = d.toString().trim();
      if (s) {
        errLogs += s + '\n';
        console.log('[scrcpy-native:err]', s);
      }
    });

    currentDeviceState.nativeScrcpyActive = true;
    broadcastJson({ type: 'native_scrcpy_status', active: true });

    proc.on('exit', async (code) => {
      if (nativeScrcpyProc === proc) {
        nativeScrcpyProc = null;
        currentDeviceState.nativeScrcpyActive = false;
        if (code && code !== 0 && errLogs.includes('failed to get feature set') && retryCount < 1) {
          console.warn('[scrcpy-native] 遇到 failed to get feature set，正在重置 ADB 并自动重试...');
          await restartAdbWithLibusb();
          const st = await pollDeviceStatus();
          if (st.adbState === 'device' && st.serial) {
            launchNativeScrcpy(st.serial, config, retryCount + 1);
            return;
          }
        }
        broadcastJson({
          type: 'native_scrcpy_status',
          active: false,
          code,
          error: code && code !== 0 && errLogs.trim() ? errLogs.trim() : null
        });
      }
    });
  } finally {
    isLaunchingNative = false;
  }
}

function stopNativeScrcpy() {
  if (nativeScrcpyProc) {
    try {
      nativeScrcpyProc.kill();
    } catch (_) {}
    nativeScrcpyProc = null;
  }
  currentDeviceState.nativeScrcpyActive = false;
  broadcastJson({ type: 'native_scrcpy_status', active: false });
}

// ============================================================================
// WebSocket Client Registry & Broadcast
// ============================================================================
const wsClients = new Set();
const scrcpySession = new ScrcpyDirectSession();

function broadcastJson(obj) {
  for (const client of wsClients) {
    client.sendJson(obj);
  }
}

function broadcastBinary(buf, isDroppable = false) {
  for (const client of wsClients) {
    client.sendBinary(buf, isDroppable);
  }
}

// ============================================================================
// HTTP & WebSocket Server
// ============================================================================
const MIME_TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.js': 'application/javascript; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.png': 'image/png',
  '.svg': 'image/svg+xml'
};

function readJsonBody(req) {
  return new Promise((resolve) => {
    let body = '';
    req.on('data', (c) => (body += c));
    req.on('end', () => {
      try {
        resolve(JSON.parse(body || '{}'));
      } catch (_) {
        resolve({});
      }
    });
  });
}

const server = http.createServer(async (req, res) => {
  const urlObj = new URL(req.url, `http://localhost:${PORT}`);
  const pathname = urlObj.pathname;

  // API endpoints
  if (pathname === '/api/status' && req.method === 'GET') {
    const status = await pollDeviceStatus();
    res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
    return res.end(JSON.stringify(status));
  }

  if (pathname === '/api/stream/start' && req.method === 'POST') {
    const body = await readJsonBody(req);
    await pollDeviceStatus();
    if (currentDeviceState.adbState !== 'device' || !currentDeviceState.serial) {
      res.writeHead(400, { 'Content-Type': 'application/json; charset=utf-8' });
      return res.end(
        JSON.stringify({
          ok: false,
          error: '未检测到已授权的小米 15 设备，请确认已用 Type-C 连接并开启「USB 调试」与「USB 调试（安全设置）」'
        })
      );
    }
    try {
      await scrcpySession.start(currentDeviceState.serial, body);
      res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
      return res.end(
        JSON.stringify({
          ok: true,
          width: scrcpySession.width,
          height: scrcpySession.height,
          deviceName: scrcpySession.deviceName
        })
      );
    } catch (err) {
      res.writeHead(500, { 'Content-Type': 'application/json; charset=utf-8' });
      return res.end(JSON.stringify({ ok: false, error: err.message }));
    }
  }

  if (pathname === '/api/stream/stop' && req.method === 'POST') {
    await scrcpySession.stop(true);
    res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
    return res.end(JSON.stringify({ ok: true }));
  }

  if (pathname === '/api/adb/reset' && req.method === 'POST') {
    await restartAdbWithLibusb();
    const status = await pollDeviceStatus();
    res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
    return res.end(JSON.stringify({ ok: true, device: status }));
  }

  if (pathname === '/api/native/start' && req.method === 'POST') {
    const body = await readJsonBody(req);
    await pollDeviceStatus();
    if (!body.otgMode && (currentDeviceState.adbState !== 'device' || !currentDeviceState.serial)) {
      res.writeHead(400, { 'Content-Type': 'application/json; charset=utf-8' });
      return res.end(
        JSON.stringify({
          ok: false,
          error: '未检测到已授权的小米 15，请先连接 Type-C 并开启 USB 调试'
        })
      );
    }
    await launchNativeScrcpy(currentDeviceState.serial, body);
    res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
    return res.end(JSON.stringify({ ok: true }));
  }

  if (pathname === '/api/native/stop' && req.method === 'POST') {
    stopNativeScrcpy();
    res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
    return res.end(JSON.stringify({ ok: true }));
  }

  if (pathname === '/api/screenshot' && req.method === 'GET') {
    await pollDeviceStatus();
    if (currentDeviceState.adbState !== 'device' || !currentDeviceState.serial) {
      res.writeHead(400, { 'Content-Type': 'application/json; charset=utf-8' });
      return res.end(JSON.stringify({ ok: false, error: '手机未连接' }));
    }
    try {
      const pngBuffer = await runAdb(['-s', currentDeviceState.serial, 'exec-out', 'screencap', '-p'], 8000, 'buffer');
      res.writeHead(200, {
        'Content-Type': 'image/png',
        'Content-Disposition': `attachment; filename="xiaomi15-${Date.now()}.png"`
      });
      return res.end(pngBuffer);
    } catch (err) {
      res.writeHead(500, { 'Content-Type': 'application/json; charset=utf-8' });
      return res.end(JSON.stringify({ ok: false, error: err.message }));
    }
  }

  if (pathname === '/api/keymaps' && req.method === 'GET') {
    if (fs.existsSync(KEYMAPS_FILE)) {
      res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
      return res.end(fs.readFileSync(KEYMAPS_FILE, 'utf8'));
    }
    res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
    return res.end(JSON.stringify({ customPresets: null }));
  }

  if (pathname === '/api/keymaps' && req.method === 'POST') {
    const body = await readJsonBody(req);
    fs.writeFileSync(KEYMAPS_FILE, JSON.stringify(body, null, 2), 'utf8');
    res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
    return res.end(JSON.stringify({ ok: true }));
  }

  // Static files
  let filePath = path.join(PUBLIC_DIR, pathname === '/' ? 'index.html' : pathname);
  if (!filePath.startsWith(PUBLIC_DIR)) {
    res.writeHead(403);
    return res.end('Forbidden');
  }

  fs.readFile(filePath, (err, content) => {
    if (err) {
      res.writeHead(404);
      return res.end('Not found');
    }
    const ext = path.extname(filePath).toLowerCase();
    res.writeHead(200, { 'Content-Type': MIME_TYPES[ext] || 'application/octet-stream' });
    res.end(content);
  });
});

// Upgrade HTTP connection to WebSocket
server.on('upgrade', (req, socket) => {
  const key = req.headers['sec-websocket-key'];
  if (!key) {
    socket.destroy();
    return;
  }
  const acceptKey = crypto
    .createHash('sha1')
    .update(key + '258EAFA5-E914-47DA-95CA-C5AB0DC85B11')
    .digest('base64');

  socket.write(
    [
      'HTTP/1.1 101 Switching Protocols',
      'Upgrade: websocket',
      'Connection: Upgrade',
      `Sec-WebSocket-Accept: ${acceptKey}`,
      '\r\n'
    ].join('\r\n')
  );

  const ws = new SimpleWebSocket(socket);
  wsClients.add(ws);

  // Send initial state and cached H.264 SPS/PPS + Keyframe if stream is already running
  ws.sendJson({
    type: 'init',
    device: currentDeviceState,
    stream: {
      active: scrcpySession.active,
      width: scrcpySession.width,
      height: scrcpySession.height,
      deviceName: scrcpySession.deviceName,
      options: scrcpySession.options
    }
  });

  if (scrcpySession.active && scrcpySession.spsPpsConfig) {
    const cfgPacket = Buffer.allocUnsafe(5 + scrcpySession.spsPpsConfig.length);
    cfgPacket[0] = 1; // isConfig
    cfgPacket.writeUInt16BE(scrcpySession.width, 1);
    cfgPacket.writeUInt16BE(scrcpySession.height, 3);
    scrcpySession.spsPpsConfig.copy(cfgPacket, 5);
    ws.sendBinary(cfgPacket);

    if (scrcpySession.lastKeyframe) {
      ws.sendBinary(scrcpySession.lastKeyframe);
    }
  }

  ws.onMessage = (msg, isBinary) => {
    if (isBinary) return;
    try {
      const data = JSON.parse(msg);
      if (data.type === 'touch') {
        scrcpySession.injectTouch(
          Number(data.action),
          Number(data.pointerId || 0),
          Number(data.x),
          Number(data.y),
          Number(data.width || scrcpySession.width),
          Number(data.height || scrcpySession.height)
        );
      } else if (data.type === 'keycode') {
        if (data.action === 'tap') {
          scrcpySession.injectKeycode(0, Number(data.keycode));
          setTimeout(() => scrcpySession.injectKeycode(1, Number(data.keycode)), 30);
        } else {
          scrcpySession.injectKeycode(Number(data.action || 0), Number(data.keycode));
        }
      } else if (data.type === 'scroll') {
        scrcpySession.injectScroll(
          Number(data.x),
          Number(data.y),
          Number(data.width || scrcpySession.width),
          Number(data.height || scrcpySession.height),
          Number(data.hScroll || 0),
          Number(data.vScroll || 0)
        );
      } else if (data.type === 'screen_power') {
        scrcpySession.setScreenPowerMode(Boolean(data.on));
      }
    } catch (_) {}
  };

  ws.onClose = () => {
    wsClients.delete(ws);
  };
});

// Background device polling every 2 seconds
setInterval(async () => {
  const prevAdbState = currentDeviceState.adbState;
  const prevSerial = currentDeviceState.serial;
  const state = await pollDeviceStatus();
  if (state.adbState !== prevAdbState || state.serial !== prevSerial || wsClients.size > 0) {
    broadcastJson({ type: 'device_status', device: state });
  }
}, 2000);

server.listen(PORT, async () => {
  console.log(`\n============================================================`);
  console.log(`  DroidDeck — Type-C 有线投屏 & 键鼠映射中心已启动！`);
  console.log(`  控制台地址: http://localhost:${PORT}`);
  console.log(`============================================================\n`);
  await ensureAdbReady();
  const st = await pollDeviceStatus();
  if (st.adbState === 'device' && st.serial && !scrcpySession.active) {
    scrcpySession.start(st.serial, { maxSize: 1920, maxFps: 60, bitRate: 8000000 }).catch((e) => {
      console.warn('[auto-start error]', e.message);
    });
  }
});
