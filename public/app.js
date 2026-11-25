// ============================================================================
// 小米 15 Type-C 有线极速投屏 & 键鼠映射前端核心引擎
// 包含:
// 1. WebCodecs H.264 硬件加速低延迟解码器 (支持横竖屏动态旋转自适应)
// 2. 多点触控键鼠映射引擎 (WASD 8方向平滑摇杆 + PointerLock 360° 视角准星 + 多指并发技能键)
// 3. 可视化键位拖拽编辑器与预设管理
// ============================================================================

const DEFAULT_PRESETS = {
  moba: {
    name: 'MOBA 预设（王者荣耀 / LOL手游）',
    preferredOrientation: 'landscape',
    items: [
      { id: 'joy_move', type: 'joystick', x: 0.16, y: 0.75, radius: 0.12, label: 'WASD 走位' },
      { id: 'atk_main', type: 'tap', x: 0.88, y: 0.78, code: 'Space', keyName: 'Space', label: '普攻' },
      { id: 'atk_minion', type: 'tap', x: 0.80, y: 0.88, code: 'KeyC', keyName: 'C', label: '补刀' },
      { id: 'atk_tower', type: 'tap', x: 0.93, y: 0.62, code: 'KeyV', keyName: 'V', label: '推塔' },
      { id: 'skill_1', type: 'tap', x: 0.72, y: 0.82, code: 'KeyQ', keyName: 'Q', label: '一技能' },
      { id: 'skill_2', type: 'tap', x: 0.77, y: 0.63, code: 'KeyE', keyName: 'E', label: '二技能' },
      { id: 'skill_3', type: 'tap', x: 0.86, y: 0.49, code: 'KeyR', keyName: 'R', label: '三技能' },
      { id: 'skill_sum', type: 'tap', x: 0.64, y: 0.86, code: 'KeyF', keyName: 'F', label: '召唤师技' },
      { id: 'skill_recall', type: 'tap', x: 0.56, y: 0.86, code: 'KeyB', keyName: 'B', label: '回城' },
      { id: 'skill_heal', type: 'tap', x: 0.60, y: 0.86, code: 'KeyG', keyName: 'G', label: '恢复' },
      { id: 'item_active', type: 'tap', x: 0.84, y: 0.34, code: 'Digit1', keyName: '1', label: '主动装' },
      { id: 'shop_buy', type: 'tap', x: 0.08, y: 0.42, code: 'Tab', keyName: 'Tab', label: '快捷出装' }
    ]
  },
  fps: {
    name: 'FPS 射击预设（和平精英 / 暗区突围 / 使命召唤）',
    preferredOrientation: 'landscape',
    items: [
      { id: 'joy_move', type: 'joystick', x: 0.17, y: 0.73, radius: 0.13, label: 'WASD 移动' },
      { id: 'aim_look', type: 'aim', x: 0.68, y: 0.50, code: 'Backquote', keyName: '~', label: '准星视角' },
      { id: 'fire_lmb', type: 'tap', x: 0.84, y: 0.72, code: 'MouseLeft', keyName: '左键', label: '开火' },
      { id: 'scope_rmb', type: 'tap', x: 0.92, y: 0.56, code: 'MouseRight', keyName: '右键', label: '开镜' },
      { id: 'jump_space', type: 'tap', x: 0.93, y: 0.74, code: 'Space', keyName: 'Space', label: '跳跃' },
      { id: 'crouch_c', type: 'tap', x: 0.87, y: 0.88, code: 'KeyC', keyName: 'C', label: '下蹲' },
      { id: 'prone_z', type: 'tap', x: 0.94, y: 0.88, code: 'KeyZ', keyName: 'Z', label: '趴下' },
      { id: 'reload_r', type: 'tap', x: 0.77, y: 0.88, code: 'KeyR', keyName: 'R', label: '换弹' },
      { id: 'interact_f', type: 'tap', x: 0.68, y: 0.36, code: 'KeyF', keyName: 'F', label: '拾取/互动' },
      { id: 'peek_q', type: 'tap', x: 0.28, y: 0.52, code: 'KeyQ', keyName: 'Q', label: '左探头' },
      { id: 'peek_e', type: 'tap', x: 0.34, y: 0.52, code: 'KeyE', keyName: 'E', label: '右探头' },
      { id: 'bag_tab', type: 'tap', x: 0.08, y: 0.88, code: 'Tab', keyName: 'Tab', label: '背包' },
      { id: 'weapon_1', type: 'tap', x: 0.45, y: 0.88, code: 'Digit1', keyName: '1', label: '主武器1' },
      { id: 'weapon_2', type: 'tap', x: 0.55, y: 0.88, code: 'Digit2', keyName: '2', label: '主武器2' }
    ]
  },
  arpg: {
    name: '动作开放世界（原神 / 鸣潮 / 绝区零）',
    preferredOrientation: 'landscape',
    items: [
      { id: 'joy_move', type: 'joystick', x: 0.16, y: 0.74, radius: 0.12, label: 'WASD 移动' },
      { id: 'aim_look', type: 'aim', x: 0.62, y: 0.48, code: 'Backquote', keyName: '~', label: '视角旋转' },
      { id: 'atk_lmb', type: 'tap', x: 0.86, y: 0.76, code: 'MouseLeft', keyName: '左键', label: '普攻/重击' },
      { id: 'dash_shift', type: 'tap', x: 0.93, y: 0.78, code: 'ShiftLeft', keyName: 'Shift', label: '冲刺闪避' },
      { id: 'jump_space', type: 'tap', x: 0.93, y: 0.60, code: 'Space', keyName: 'Space', label: '跳跃' },
      { id: 'skill_e', type: 'tap', x: 0.77, y: 0.82, code: 'KeyE', keyName: 'E', label: '元素战技' },
      { id: 'ult_q', type: 'tap', x: 0.69, y: 0.84, code: 'KeyQ', keyName: 'Q', label: '元素爆发' },
      { id: 'pick_f', type: 'tap', x: 0.64, y: 0.56, code: 'KeyF', keyName: 'F', label: '拾取/对话' },
      { id: 'char_1', type: 'tap', x: 0.93, y: 0.22, code: 'Digit1', keyName: '1', label: '角色1' },
      { id: 'char_2', type: 'tap', x: 0.93, y: 0.32, code: 'Digit2', keyName: '2', label: '角色2' },
      { id: 'char_3', type: 'tap', x: 0.93, y: 0.42, code: 'Digit3', keyName: '3', label: '角色3' },
      { id: 'char_4', type: 'tap', x: 0.93, y: 0.52, code: 'Digit4', keyName: '4', label: '角色4' }
    ]
  }
};

// State
let ws = null;
let streamActive = false;
let deviceWidth = 1200;
let deviceHeight = 2670;
let controlMode = 'touch'; // 'touch' | 'play' | 'edit'
let currentPresetKey = 'moba';
let presets = JSON.parse(JSON.stringify(DEFAULT_PRESETS));
let showOverlay = true;
let mouseSensitivity = 1.2;

// Stats
let frameCount = 0;
let byteCount = 0;
let lastStatTime = performance.now();

// DOM references
const canvas = document.getElementById('screenCanvas');
const ctx = canvas.getContext('2d', { alpha: false, desynchronized: true });
const phoneFrame = document.getElementById('phoneFrame');
const stageViewport = document.getElementById('stageViewport');
const keymapOverlay = document.getElementById('keymapOverlay');
const standbyOverlay = document.getElementById('standbyOverlay');

// ============================================================================
// 1. WebCodecs H.264 Low-Latency Hardware Decoder
// ============================================================================
class H264HardwarePlayer {
  constructor() {
    this.decoder = null;
    this.spsPps = null;
    this.configuredCodec = null;
    this.timestamp = 0;
    this.needsKeyframe = true;
  }

  _extractAvcCodecString(annexB) {
    // Search for SPS NAL unit (type 7 = 0x67 / 0x27)
    for (let i = 0; i < annexB.length - 7; i++) {
      let offset = -1;
      if (annexB[i] === 0 && annexB[i + 1] === 0 && annexB[i + 2] === 1) offset = i + 3;
      else if (annexB[i] === 0 && annexB[i + 1] === 0 && annexB[i + 2] === 0 && annexB[i + 3] === 1) offset = i + 4;

      if (offset !== -1 && (annexB[offset] & 0x1f) === 7) {
        const profile = annexB[offset + 1].toString(16).padStart(2, '0');
        const compat = annexB[offset + 2].toString(16).padStart(2, '0');
        const level = annexB[offset + 3].toString(16).padStart(2, '0');
        return `avc1.${profile}${compat}${level}`;
      }
    }
    return 'avc1.64002a';
  }

  _ensureDecoder(codecStr) {
    if (this.decoder && this.decoder.state === 'configured' && this.configuredCodec === codecStr) {
      return;
    }
    if (this.decoder) {
      try {
        this.decoder.close();
      } catch (_) {}
    }

    this.decoder = new VideoDecoder({
      output: (frame) => {
        if (frame.displayWidth && frame.displayHeight) {
          if (canvas.width !== frame.displayWidth || canvas.height !== frame.displayHeight) {
            updateStageDimensions(frame.displayWidth, frame.displayHeight);
          }
        }
        ctx.drawImage(frame, 0, 0, canvas.width, canvas.height);
        frame.close();
        frameCount++;
      },
      error: (err) => {
        console.warn('[VideoDecoder Error] resetting decoder for next keyframe:', err);
        this.decoder = null;
        this.configuredCodec = null;
        this.needsKeyframe = true;
      }
    });

    this.decoder.configure({
      codec: codecStr,
      optimizeForLatency: true
    });
    this.configuredCodec = codecStr;
    this.needsKeyframe = true;
  }

  feedPacket(arrayBuffer) {
    const view = new Uint8Array(arrayBuffer);
    if (view.length <= 5) return;
    byteCount += view.length;

    const flags = view[0];
    const isConfig = (flags & 1) !== 0;
    const isKeyframe = (flags & 2) !== 0;
    const w = (view[1] << 8) | view[2];
    const h = (view[3] << 8) | view[4];
    const payload = view.subarray(5);

    if (w > 0 && h > 0 && (w !== deviceWidth || h !== deviceHeight)) {
      updateStageDimensions(w, h);
    }

    if (isConfig) {
      this.spsPps = new Uint8Array(payload);
      const codecStr = this._extractAvcCodecString(this.spsPps);
      this._ensureDecoder(codecStr);
      return;
    }

    if (!this.decoder || this.decoder.state !== 'configured') {
      if (this.spsPps) {
        this._ensureDecoder(this._extractAvcCodecString(this.spsPps));
      } else if (isKeyframe) {
        this._ensureDecoder(this._extractAvcCodecString(payload));
      } else {
        return;
      }
    }

    if (this.needsKeyframe) {
      if (!isKeyframe) return;
      this.needsKeyframe = false;
    }

    // Prevent WebCodecs queue backlog only on severe stall (> 30 queued frames)
    if (this.decoder.decodeQueueSize > 30 && !isKeyframe) {
      console.warn('[WebCodecs] queue backlog:', this.decoder.decodeQueueSize, 'resyncing to next keyframe');
      this.needsKeyframe = true;
      return;
    }

    let chunkData = payload;
    if (isKeyframe && this.spsPps) {
      // Prepend SPS/PPS config to Annex-B IDR keyframe for WebCodecs
      const combined = new Uint8Array(this.spsPps.length + payload.length);
      combined.set(this.spsPps, 0);
      combined.set(payload, this.spsPps.length);
      chunkData = combined;
    }

    try {
      const chunk = new EncodedVideoChunk({
        type: isKeyframe ? 'key' : 'delta',
        timestamp: (this.timestamp += 16666),
        data: chunkData
      });
      this.decoder.decode(chunk);
    } catch (e) {
      console.warn('Decode chunk failed, resetting decoder:', e);
      this.decoder = null;
      this.configuredCodec = null;
      this.needsKeyframe = true;
    }
  }

  reset() {
    if (this.decoder) {
      try {
        this.decoder.close();
      } catch (_) {}
      this.decoder = null;
    }
    this.spsPps = null;
    this.configuredCodec = null;
    this.needsKeyframe = true;
  }
}

const h264Player = new H264HardwarePlayer();

// ============================================================================
// 2. Stage Layout & Orientation Adaptive Sizing
// ============================================================================
function updateStageDimensions(w, h) {
  deviceWidth = w;
  deviceHeight = h;
  canvas.width = w;
  canvas.height = h;
  document.getElementById('hudResolution').textContent = `分辨率: ${w}×${h}`;
  fitPhoneFrameToViewport();
}

function fitPhoneFrameToViewport() {
  const rect = stageViewport.getBoundingClientRect();
  const availW = Math.max(240, rect.width - 28);
  const availH = Math.max(240, rect.height - 28);

  const orientMode = document.getElementById('selOrientation').value;
  let aspectW = deviceWidth;
  let aspectH = deviceHeight;

  if (orientMode === 'landscape' && aspectW < aspectH) {
    [aspectW, aspectH] = [aspectH, aspectW];
  } else if (orientMode === 'portrait' && aspectW > aspectH) {
    [aspectW, aspectH] = [aspectH, aspectW];
  }

  const ratio = aspectW / aspectH;
  let targetW = availH * ratio;
  let targetH = availH;

  if (targetW > availW) {
    targetW = availW;
    targetH = availW / ratio;
  }

  phoneFrame.style.width = `${Math.round(targetW)}px`;
  phoneFrame.style.height = `${Math.round(targetH)}px`;
  renderKeymapOverlay();
}

window.addEventListener('resize', fitPhoneFrameToViewport);
document.getElementById('selOrientation').addEventListener('change', fitPhoneFrameToViewport);

// ============================================================================
// 3. WebSocket Connection & Device Status Updates
// ============================================================================
function connectWebSocket() {
  const proto = location.protocol === 'https:' ? 'wss:' : 'ws:';
  ws = new WebSocket(`${proto}//${location.host}`);
  ws.binaryType = 'arraybuffer';

  ws.onmessage = (evt) => {
    if (evt.data instanceof ArrayBuffer) {
      if (!streamActive) {
        setStreamActiveUI(true);
      }
      h264Player.feedPacket(evt.data);
      return;
    }

    try {
      const msg = JSON.parse(evt.data);
      if (msg.type === 'init') {
        updateDeviceStatusUI(msg.device);
        if (msg.stream && msg.stream.active) {
          updateStageDimensions(msg.stream.width || 1200, msg.stream.height || 2670);
          setStreamActiveUI(true);
        }
      } else if (msg.type === 'device_status') {
        updateDeviceStatusUI(msg.device);
      } else if (msg.type === 'stream_status') {
        if (msg.active) {
          updateStageDimensions(msg.width || 1200, msg.height || 2670);
          setStreamActiveUI(true);
        } else {
          setStreamActiveUI(false);
        }
      } else if (msg.type === 'stream_reconnecting') {
        h264Player.needsKeyframe = true;
        document.getElementById('hudStreamState').textContent = '🟡 连接波动，正在自动重连...';
      } else if (msg.type === 'native_scrcpy_status') {
        if (msg.error) {
          alert(`原生 Scrcpy 窗口异常退出：\n${msg.error}`);
        }
      } else if (msg.type === 'stream_meta') {
        updateStageDimensions(msg.width, msg.height);
      }
    } catch (_) {}
  };

  ws.onclose = () => {
    setTimeout(connectWebSocket, 1500);
  };
}

function sendWs(payload) {
  if (ws && ws.readyState === WebSocket.OPEN) {
    ws.send(JSON.stringify(payload));
  }
}

function setStreamActiveUI(active) {
  streamActive = active;
  standbyOverlay.style.display = active ? 'none' : 'flex';
  document.getElementById('btnStartWebStream').style.display = active ? 'none' : 'inline-flex';
  document.getElementById('btnStopWebStream').style.display = active ? 'inline-flex' : 'none';
  document.getElementById('hudStreamState').textContent = active ? '🟢 实时投屏中 (Type-C)' : '⏹ 待启动投屏';
  if (!active) {
    h264Player.reset();
  }
}

function updateDeviceStatusUI(dev) {
  if (!dev) return;
  const badge = document.getElementById('connBadge');
  const guide = document.getElementById('miSetupGuide');

  if (dev.adbState === 'device') {
    badge.className = 'badge badge-ok';
    badge.textContent = '已就绪 (Type-C ADB)';
    document.getElementById('infoModel').textContent = dev.marketName || dev.model || '小米 15';
    document.getElementById('infoOs').textContent = `${dev.hyperOsVersion || 'HyperOS'} (Android ${dev.androidVersion || '15'})`;
    document.getElementById('infoRes').textContent = dev.resolution || '1200x2670';
    document.getElementById('infoBat').textContent = dev.batteryLevel != null ? `${dev.batteryLevel}%` : '--';
    document.getElementById('standbyTitle').textContent = `已连接 ${dev.marketName || '小米 15'}！`;
    document.getElementById('standbyDesc').textContent = '手机已通过 Type-C 授权就绪，点击下方按钮即可开启超低延迟投屏与键鼠映射。';
    guide.open = false;
  } else if (dev.adbState === 'unauthorized') {
    badge.className = 'badge badge-warn';
    badge.textContent = '等待手机点击授权';
    document.getElementById('infoModel').textContent = '小米 15 (待授权)';
    document.getElementById('standbyTitle').textContent = '请在小米 15 屏幕上点击「允许调试」';
    document.getElementById('standbyDesc').textContent = '手机端已弹出“允许 USB 调试吗？”窗口，请勾选「始终允许使用这台计算机进行调试」并点击确定。';
    guide.open = true;
  } else if (dev.usbDetected) {
    badge.className = 'badge badge-warn';
    badge.textContent = '已插线 · 未开USB调试';
    document.getElementById('infoModel').textContent = '检测到 USB 连接';
    document.getElementById('standbyTitle').textContent = '已检测到 USB 连接，请开启「USB 调试」';
    document.getElementById('standbyDesc').textContent = '请按照左侧 3 步引导，在小米 15「开发者选项」中打开「USB 调试」和「USB 调试（安全设置）」。';
    guide.open = true;
  } else {
    badge.className = 'badge badge-err';
    badge.textContent = '未检测到手机';
    document.getElementById('infoModel').textContent = '未连接';
    document.getElementById('infoOs').textContent = '--';
    document.getElementById('infoRes').textContent = '--';
    document.getElementById('infoBat').textContent = '--';
  }
}

// ============================================================================
// 4. Direct Mouse Touch Control (Pointer ID = 1)
// ============================================================================
let isMouseDownTouch = false;

function clientToDeviceCoords(clientX, clientY) {
  const rect = phoneFrame.getBoundingClientRect();
  const normX = Math.max(0, Math.min(1, (clientX - rect.left) / rect.width));
  const normY = Math.max(0, Math.min(1, (clientY - rect.top) / rect.height));
  return {
    x: Math.round(normX * deviceWidth),
    y: Math.round(normY * deviceHeight),
    normX,
    normY
  };
}

phoneFrame.addEventListener('mousedown', (e) => {
  if (controlMode === 'edit') return;
  if (controlMode === 'play') {
    handlePlayModeMouseDown(e);
    return;
  }
  if (e.button !== 0) return;
  isMouseDownTouch = true;
  const pt = clientToDeviceCoords(e.clientX, e.clientY);
  sendWs({ type: 'touch', action: 0, pointerId: 1, x: pt.x, y: pt.y, width: deviceWidth, height: deviceHeight });
});

window.addEventListener('mousemove', (e) => {
  if (controlMode === 'play') {
    handlePlayModeMouseMove(e);
    return;
  }
  if (!isMouseDownTouch || controlMode !== 'touch') return;
  const pt = clientToDeviceCoords(e.clientX, e.clientY);
  sendWs({ type: 'touch', action: 2, pointerId: 1, x: pt.x, y: pt.y, width: deviceWidth, height: deviceHeight });
});

window.addEventListener('mouseup', (e) => {
  if (controlMode === 'play') {
    handlePlayModeMouseUp(e);
    return;
  }
  if (!isMouseDownTouch) return;
  isMouseDownTouch = false;
  const pt = clientToDeviceCoords(e.clientX, e.clientY);
  sendWs({ type: 'touch', action: 1, pointerId: 1, x: pt.x, y: pt.y, width: deviceWidth, height: deviceHeight });
});

phoneFrame.addEventListener(
  'wheel',
  (e) => {
    if (controlMode !== 'touch') return;
    e.preventDefault();
    const pt = clientToDeviceCoords(e.clientX, e.clientY);
    const vScroll = e.deltaY < 0 ? 1 : -1;
    sendWs({ type: 'scroll', x: pt.x, y: pt.y, width: deviceWidth, height: deviceHeight, hScroll: 0, vScroll });
  },
  { passive: false }
);

phoneFrame.addEventListener('contextmenu', (e) => e.preventDefault());

// ============================================================================
// 5. Multi-Touch Game Keymapping Engine (WASD Joystick + Aim Look + Skill Keys)
// ============================================================================
const POINTER_JOYSTICK = 10;
const POINTER_AIM = 11;
const POINTER_TAP_BASE = 20;

const wasdPressed = { KeyW: false, KeyA: false, KeyS: false, KeyD: false };
let joystickActive = false;
const activeTapKeys = new Map(); // code -> { pointerId, x, y, itemId }
let aimLocked = false;
let aimCurrentX = 0;
let aimCurrentY = 0;

function getCurrentPresetItems() {
  return presets[currentPresetKey]?.items || [];
}

function updateJoystickState() {
  const joyItem = getCurrentPresetItems().find((it) => it.type === 'joystick');
  if (!joyItem) return;

  let dx = 0;
  let dy = 0;
  if (wasdPressed.KeyA) dx -= 1;
  if (wasdPressed.KeyD) dx += 1;
  if (wasdPressed.KeyW) dy -= 1;
  if (wasdPressed.KeyS) dy += 1;

  const centerX = Math.round(joyItem.x * deviceWidth);
  const centerY = Math.round(joyItem.y * deviceHeight);
  const radiusPx = Math.round((joyItem.radius || 0.11) * Math.min(deviceWidth, deviceHeight));

  const stickEl = document.querySelector(`[data-item-id="${joyItem.id}"] .km-joystick-stick`);

  if (dx === 0 && dy === 0) {
    if (joystickActive) {
      joystickActive = false;
      sendWs({
        type: 'touch',
        action: 1, // UP
        pointerId: POINTER_JOYSTICK,
        x: centerX,
        y: centerY,
        width: deviceWidth,
        height: deviceHeight
      });
    }
    if (stickEl) stickEl.style.transform = 'translate(0px, 0px)';
    return;
  }

  // Normalize vector
  const len = Math.hypot(dx, dy);
  const normDx = dx / len;
  const normDy = dy / len;
  const targetX = Math.round(centerX + normDx * radiusPx);
  const targetY = Math.round(centerY + normDy * radiusPx);

  if (!joystickActive) {
    joystickActive = true;
    // Touch DOWN at joystick center first
    sendWs({
      type: 'touch',
      action: 0,
      pointerId: POINTER_JOYSTICK,
      x: centerX,
      y: centerY,
      width: deviceWidth,
      height: deviceHeight
    });
  }

  // Move to direction offset
  sendWs({
    type: 'touch',
    action: 2,
    pointerId: POINTER_JOYSTICK,
    x: targetX,
    y: targetY,
    width: deviceWidth,
    height: deviceHeight
  });

  if (stickEl) {
    stickEl.style.transform = `translate(${Math.round(normDx * 22)}px, ${Math.round(normDy * 22)}px)`;
  }
}

function toggleAimLock() {
  const aimItem = getCurrentPresetItems().find((it) => it.type === 'aim');
  if (!aimItem) return;

  if (!aimLocked) {
    phoneFrame.requestPointerLock();
  } else {
    document.exitPointerLock();
  }
}

document.addEventListener('pointerlockchange', () => {
  const aimItem = getCurrentPresetItems().find((it) => it.type === 'aim');
  const isLockedNow = document.pointerLockElement === phoneFrame;
  const aimEl = aimItem ? document.querySelector(`[data-item-id="${aimItem.id}"]`) : null;

  if (isLockedNow && aimItem) {
    aimLocked = true;
    aimCurrentX = Math.round(aimItem.x * deviceWidth);
    aimCurrentY = Math.round(aimItem.y * deviceHeight);
    sendWs({
      type: 'touch',
      action: 0,
      pointerId: POINTER_AIM,
      x: aimCurrentX,
      y: aimCurrentY,
      width: deviceWidth,
      height: deviceHeight
    });
    if (aimEl) aimEl.classList.add('locked');
  } else {
    if (aimLocked) {
      sendWs({
        type: 'touch',
        action: 1,
        pointerId: POINTER_AIM,
        x: aimCurrentX,
        y: aimCurrentY,
        width: deviceWidth,
        height: deviceHeight
      });
    }
    aimLocked = false;
    if (aimEl) aimEl.classList.remove('locked');
  }
});

function handlePlayModeMouseMove(e) {
  if (!aimLocked) return;
  const aimItem = getCurrentPresetItems().find((it) => it.type === 'aim');
  if (!aimItem) return;

  aimCurrentX += Math.round(e.movementX * mouseSensitivity * 1.6);
  aimCurrentY += Math.round(e.movementY * mouseSensitivity * 1.6);

  // Auto re-center virtual finger if approaching screen edge so 360-degree rotation never hits wall
  const marginX = Math.round(deviceWidth * 0.08);
  const marginY = Math.round(deviceHeight * 0.08);
  if (
    aimCurrentX < marginX ||
    aimCurrentX > deviceWidth - marginX ||
    aimCurrentY < marginY ||
    aimCurrentY > deviceHeight - marginY
  ) {
    sendWs({
      type: 'touch',
      action: 1,
      pointerId: POINTER_AIM,
      x: aimCurrentX,
      y: aimCurrentY,
      width: deviceWidth,
      height: deviceHeight
    });
    aimCurrentX = Math.round(aimItem.x * deviceWidth);
    aimCurrentY = Math.round(aimItem.y * deviceHeight);
    sendWs({
      type: 'touch',
      action: 0,
      pointerId: POINTER_AIM,
      x: aimCurrentX,
      y: aimCurrentY,
      width: deviceWidth,
      height: deviceHeight
    });
    return;
  }

  sendWs({
    type: 'touch',
    action: 2,
    pointerId: POINTER_AIM,
    x: aimCurrentX,
    y: aimCurrentY,
    width: deviceWidth,
    height: deviceHeight
  });
}

function triggerCodeDown(code) {
  // Check Aim lock toggle key
  const aimItem = getCurrentPresetItems().find((it) => it.type === 'aim' && it.code === code);
  if (aimItem) {
    toggleAimLock();
    return;
  }

  // Check WASD joystick
  if (code in wasdPressed) {
    if (!wasdPressed[code]) {
      wasdPressed[code] = true;
      updateJoystickState();
    }
    return;
  }

  if (activeTapKeys.has(code)) return;

  const items = getCurrentPresetItems();
  const idx = items.findIndex((it) => it.type === 'tap' && it.code === code);
  if (idx === -1) return;

  const item = items[idx];
  const pointerId = POINTER_TAP_BASE + idx;
  const x = Math.round(item.x * deviceWidth);
  const y = Math.round(item.y * deviceHeight);

  activeTapKeys.set(code, { pointerId, x, y, itemId: item.id });
  sendWs({ type: 'touch', action: 0, pointerId, x, y, width: deviceWidth, height: deviceHeight });

  const el = document.querySelector(`[data-item-id="${item.id}"]`);
  if (el) el.classList.add('pressed');
}

function triggerCodeUp(code) {
  if (code in wasdPressed) {
    if (wasdPressed[code]) {
      wasdPressed[code] = false;
      updateJoystickState();
    }
    return;
  }

  const active = activeTapKeys.get(code);
  if (!active) return;
  activeTapKeys.delete(code);

  sendWs({
    type: 'touch',
    action: 1,
    pointerId: active.pointerId,
    x: active.x,
    y: active.y,
    width: deviceWidth,
    height: deviceHeight
  });

  const el = document.querySelector(`[data-item-id="${active.itemId}"]`);
  if (el) el.classList.remove('pressed');
}

function handlePlayModeMouseDown(e) {
  const code = e.button === 0 ? 'MouseLeft' : e.button === 2 ? 'MouseRight' : 'MouseMiddle';
  const mapped = getCurrentPresetItems().some((it) => it.code === code);
  if (mapped) {
    triggerCodeDown(code);
  } else if (e.button === 0 && !aimLocked) {
    // Allow direct click passthrough if MouseLeft isn't bound
    isMouseDownTouch = true;
    const pt = clientToDeviceCoords(e.clientX, e.clientY);
    sendWs({ type: 'touch', action: 0, pointerId: 1, x: pt.x, y: pt.y, width: deviceWidth, height: deviceHeight });
  }
}

function handlePlayModeMouseUp(e) {
  const code = e.button === 0 ? 'MouseLeft' : e.button === 2 ? 'MouseRight' : 'MouseMiddle';
  if (activeTapKeys.has(code)) {
    triggerCodeUp(code);
  } else if (isMouseDownTouch) {
    isMouseDownTouch = false;
    const pt = clientToDeviceCoords(e.clientX, e.clientY);
    sendWs({ type: 'touch', action: 1, pointerId: 1, x: pt.x, y: pt.y, width: deviceWidth, height: deviceHeight });
  }
}

window.addEventListener('keydown', (e) => {
  if (bindingTargetItem) {
    e.preventDefault();
    finishKeyBind(e.code, formatKeyDisplay(e.code, e.key));
    return;
  }
  if (controlMode !== 'play') return;
  if (['INPUT', 'SELECT', 'TEXTAREA'].includes(document.activeElement?.tagName)) return;

  if (e.code === 'Tab' || e.code === 'Space' || e.code.startsWith('Key') || e.code.startsWith('Digit')) {
    e.preventDefault();
  }
  triggerCodeDown(e.code);
});

window.addEventListener('keyup', (e) => {
  if (controlMode !== 'play') return;
  triggerCodeUp(e.code);
});

function formatKeyDisplay(code, key) {
  if (code === 'Space') return 'Space';
  if (code === 'ShiftLeft' || code === 'ShiftRight') return 'Shift';
  if (code === 'Backquote') return '~';
  if (code === 'Tab') return 'Tab';
  if (code.startsWith('Key')) return code.slice(3);
  if (code.startsWith('Digit')) return code.slice(5);
  return key && key.length === 1 ? key.toUpperCase() : code;
}

// ============================================================================
// 6. Visual Keymap Overlay Renderer & Drag-and-Drop Editor
// ============================================================================
let draggingItem = null;
let dragMoved = false;
let bindingTargetItem = null;

function renderKeymapOverlay() {
  keymapOverlay.innerHTML = '';
  keymapOverlay.style.display = showOverlay || controlMode === 'edit' ? 'block' : 'none';
  keymapOverlay.classList.toggle('editable', controlMode === 'edit');

  const items = getCurrentPresetItems();
  const frameRect = phoneFrame.getBoundingClientRect();
  const minSide = Math.min(frameRect.width || 400, frameRect.height || 400);

  for (const item of items) {
    const el = document.createElement('div');
    el.className = `km-item km-${item.type}`;
    el.dataset.itemId = item.id;
    el.style.left = `${(item.x * 100).toFixed(2)}%`;
    el.style.top = `${(item.y * 100).toFixed(2)}%`;

    if (item.type === 'joystick') {
      const diameter = Math.max(74, Math.round((item.radius || 0.11) * 2 * minSide));
      el.style.width = `${diameter}px`;
      el.style.height = `${diameter}px`;
      el.innerHTML = `<div class="km-joystick-stick">WASD</div>`;
    } else if (item.type === 'aim') {
      el.innerHTML = `<div>🎯 ${item.keyName || '~'}</div><div class="km-label">${item.label || '视角'}</div>`;
    } else {
      el.innerHTML = `<div>${item.keyName || '?'}</div><div class="km-label">${item.label || ''}</div>`;
    }

    // Editor drag & click-to-rebind
    el.addEventListener('mousedown', (e) => {
      if (controlMode !== 'edit') return;
      e.stopPropagation();
      if (e.button === 2) {
        // Right click delete in edit mode
        const list = getCurrentPresetItems();
        const idx = list.findIndex((i) => i.id === item.id);
        if (idx >= 0) {
          list.splice(idx, 1);
          renderKeymapOverlay();
        }
        return;
      }
      draggingItem = item;
      dragMoved = false;
    });

    keymapOverlay.appendChild(el);
  }
}

window.addEventListener('mousemove', (e) => {
  if (controlMode !== 'edit' || !draggingItem) return;
  dragMoved = true;
  const rect = phoneFrame.getBoundingClientRect();
  draggingItem.x = Math.max(0.04, Math.min(0.96, (e.clientX - rect.left) / rect.width));
  draggingItem.y = Math.max(0.04, Math.min(0.96, (e.clientY - rect.top) / rect.height));
  const el = document.querySelector(`[data-item-id="${draggingItem.id}"]`);
  if (el) {
    el.style.left = `${(draggingItem.x * 100).toFixed(2)}%`;
    el.style.top = `${(draggingItem.y * 100).toFixed(2)}%`;
  }
});

window.addEventListener('mouseup', () => {
  if (controlMode !== 'edit' || !draggingItem) return;
  const clickedItem = draggingItem;
  draggingItem = null;
  if (!dragMoved && clickedItem.type !== 'joystick') {
    openKeyBindModal(clickedItem);
  }
});

function openKeyBindModal(item) {
  bindingTargetItem = item;
  document.getElementById('keyBindTargetLabel').textContent = `当前键位: ${item.label || item.id} (${item.keyName})`;
  document.getElementById('keyBindPreview').textContent = '请按下键盘按键...';
  document.getElementById('keyBindModal').style.display = 'flex';
}

function finishKeyBind(code, keyName) {
  if (!bindingTargetItem) return;
  bindingTargetItem.code = code;
  bindingTargetItem.keyName = keyName;
  bindingTargetItem = null;
  document.getElementById('keyBindModal').style.display = 'none';
  renderKeymapOverlay();
}

document.getElementById('btnBindMouseLeft').addEventListener('click', () => finishKeyBind('MouseLeft', '左键'));
document.getElementById('btnBindMouseRight').addEventListener('click', () => finishKeyBind('MouseRight', '右键'));
document.getElementById('btnCancelBind').addEventListener('click', () => {
  bindingTargetItem = null;
  document.getElementById('keyBindModal').style.display = 'none';
});
document.getElementById('btnDeleteKeyItem').addEventListener('click', () => {
  if (!bindingTargetItem) return;
  const list = getCurrentPresetItems();
  const idx = list.findIndex((i) => i.id === bindingTargetItem.id);
  if (idx >= 0) list.splice(idx, 1);
  bindingTargetItem = null;
  document.getElementById('keyBindModal').style.display = 'none';
  renderKeymapOverlay();
});

// ============================================================================
// 7. UI Controls & Event Bindings
// ============================================================================
async function startWebStream() {
  const maxSize = Number(document.getElementById('selMaxSize').value);
  const maxFps = Number(document.getElementById('selMaxFps').value);
  const bitRate = Number(document.getElementById('selBitRate').value);

  const btn = document.getElementById('btnStartWebStream');
  btn.textContent = '⏳ 正在连接小米 15...';
  btn.disabled = true;

  try {
    const res = await fetch('/api/stream/start', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ maxSize, maxFps, bitRate })
    });
    const data = await res.json();
    if (!data.ok) {
      alert(data.error || '启动投屏失败');
    } else {
      updateStageDimensions(data.width || 1200, data.height || 2670);
      setStreamActiveUI(true);
    }
  } catch (e) {
    alert('请求失败: ' + e.message);
  } finally {
    btn.textContent = '▶ 启动内嵌投屏（带键鼠映射图层）';
    btn.disabled = false;
  }
}

async function stopWebStream() {
  await fetch('/api/stream/stop', { method: 'POST' });
  setStreamActiveUI(false);
}

async function startNativeWindow(otgMode = false) {
  const maxSize = Number(document.getElementById('selMaxSize').value);
  const maxFps = Number(document.getElementById('selMaxFps').value);
  const bitRateM = Math.round(Number(document.getElementById('selBitRate').value) / 1000000);

  const res = await fetch('/api/native/start', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ maxSize, maxFps, bitRateM, otgMode })
  });
  const data = await res.json();
  if (!data.ok) {
    alert(data.error || '启动原生窗口失败');
  }
}

document.getElementById('btnStartWebStream').addEventListener('click', startWebStream);
document.getElementById('btnHeroStart').addEventListener('click', startWebStream);
document.getElementById('btnStopWebStream').addEventListener('click', stopWebStream);
document.getElementById('btnStartNative').addEventListener('click', () => startNativeWindow(false));
document.getElementById('btnHeroNative').addEventListener('click', () => startNativeWindow(false));
document.getElementById('btnStartOtg').addEventListener('click', () => startNativeWindow(true));

// Quick device keycodes
document.querySelectorAll('[data-keycode]').forEach((btn) => {
  btn.addEventListener('click', () => {
    const keycode = Number(btn.dataset.keycode);
    sendWs({ type: 'keycode', action: 'tap', keycode });
  });
});

document.getElementById('btnScreenOff').addEventListener('click', () => {
  sendWs({ type: 'screen_power', on: false });
});

document.getElementById('btnScreenOn').addEventListener('click', () => {
  sendWs({ type: 'screen_power', on: true });
});

document.getElementById('btnScreenshot').addEventListener('click', () => {
  window.open('/api/screenshot', '_blank');
});

document.getElementById('btnResetAdb').addEventListener('click', async () => {
  const btn = document.getElementById('btnResetAdb');
  btn.textContent = '⏳ 重置中...';
  btn.disabled = true;
  try {
    const res = await fetch('/api/adb/reset', { method: 'POST' });
    const data = await res.json();
    if (data && data.device) updateDeviceStatusUI(data.device);
  } catch (_) {
  } finally {
    btn.textContent = '🔄 重置ADB';
    btn.disabled = false;
  }
});

document.getElementById('btnFullscreenStage').addEventListener('click', () => {
  if (!document.fullscreenElement) {
    document.querySelector('.stage-container').requestFullscreen();
  } else {
    document.exitFullscreen();
  }
});

// Mode switching
document.querySelectorAll('.mode-tab').forEach((tab) => {
  tab.addEventListener('click', () => {
    document.querySelectorAll('.mode-tab').forEach((t) => t.classList.remove('active'));
    tab.classList.add('active');
    controlMode = tab.dataset.mode;

    if (document.pointerLockElement) document.exitPointerLock();

    const modeNames = {
      touch: '当前模式: 🖱️ 普通鼠标触控',
      play: '当前模式: 🎮 键鼠映射激活',
      edit: '当前模式: 🛠️ 键位可视化编辑'
    };
    document.getElementById('hudControlMode').textContent = modeNames[controlMode];
    document.getElementById('editToolbar').style.display = controlMode === 'edit' ? 'block' : 'none';
    document.getElementById('playHintBox').style.display = controlMode === 'play' ? 'block' : 'none';

    // Auto switch view layout to landscape when entering game play/edit mode if screen is still portrait
    if ((controlMode === 'play' || controlMode === 'edit') && presets[currentPresetKey]?.preferredOrientation === 'landscape') {
      document.getElementById('selOrientation').value = 'landscape';
      fitPhoneFrameToViewport();
    }
    renderKeymapOverlay();
  });
});

document.getElementById('chkShowOverlay').addEventListener('change', (e) => {
  showOverlay = e.target.checked;
  renderKeymapOverlay();
});

document.getElementById('selPreset').addEventListener('change', (e) => {
  currentPresetKey = e.target.value;
  renderKeymapOverlay();
});

document.getElementById('rngSensitivity').addEventListener('input', (e) => {
  mouseSensitivity = Number(e.target.value);
});

document.getElementById('rngJoystickRadius').addEventListener('input', (e) => {
  const r = Number(e.target.value) / 100;
  const joy = getCurrentPresetItems().find((it) => it.type === 'joystick');
  if (joy) {
    joy.radius = r;
    renderKeymapOverlay();
  }
});

// Add / save keymap items
document.getElementById('btnAddTapKey').addEventListener('click', () => {
  const label = prompt('请输入该按键的用途名称（例如：换弹、大招、跳跃）：', '技能') || '按键';
  const newItem = {
    id: 'tap_' + Date.now(),
    type: 'tap',
    x: 0.5,
    y: 0.5,
    code: 'KeyG',
    keyName: 'G',
    label
  };
  getCurrentPresetItems().push(newItem);
  renderKeymapOverlay();
  openKeyBindModal(newItem);
});

document.getElementById('btnAddJoystick').addEventListener('click', () => {
  const items = getCurrentPresetItems();
  if (items.some((i) => i.type === 'joystick')) {
    alert('当前预设已有 WASD 摇杆，可直接拖拽调整其位置');
    return;
  }
  items.push({ id: 'joy_' + Date.now(), type: 'joystick', x: 0.18, y: 0.75, radius: 0.12, label: 'WASD 摇杆' });
  renderKeymapOverlay();
});

document.getElementById('btnAddAim').addEventListener('click', () => {
  const items = getCurrentPresetItems();
  if (items.some((i) => i.type === 'aim')) {
    alert('当前预设已有视角准星控制区，可直接拖拽调整其位置');
    return;
  }
  items.push({ id: 'aim_' + Date.now(), type: 'aim', x: 0.68, y: 0.5, code: 'Backquote', keyName: '~', label: '准星视角' });
  renderKeymapOverlay();
});

document.getElementById('btnResetPreset').addEventListener('click', () => {
  presets[currentPresetKey] = JSON.parse(JSON.stringify(DEFAULT_PRESETS[currentPresetKey]));
  renderKeymapOverlay();
});

document.getElementById('btnSaveKeymap').addEventListener('click', async () => {
  await fetch('/api/keymaps', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ customPresets: presets })
  });
  alert('✅ 键鼠映射方案已保存！');
});

// Load saved custom keymaps on boot
fetch('/api/keymaps')
  .then((r) => r.json())
  .then((data) => {
    if (data && data.customPresets) {
      presets = data.customPresets;
      renderKeymapOverlay();
    }
  })
  .catch(() => {});

// HUD FPS & Bitrate telemetry timer
setInterval(() => {
  const now = performance.now();
  const dt = (now - lastStatTime) / 1000;
  if (dt > 0) {
    const fps = Math.round(frameCount / dt);
    const mbps = ((byteCount * 8) / dt / 1000000).toFixed(1);
    document.getElementById('hudFps').textContent = `实时帧率: ${fps} FPS`;
    document.getElementById('hudBitrate').textContent = `码率: ${mbps} Mbps`;
    frameCount = 0;
    byteCount = 0;
    lastStatTime = now;
  }
}, 1000);

// Initialize
fitPhoneFrameToViewport();
connectWebSocket();
