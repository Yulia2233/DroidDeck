# DroidDeck

**Android Type-C 有线极速投屏 & 原生桌面键鼠映射客户端**（针对小米 / HyperOS 深度适配与稳定性调优）。

无需安装 Node.js、无需浏览器、无需在手机端安装任何第三方 App。Windows 用户可通过 **`DroidDeck-Setup-1.0.0.exe` 安装包** 一键安装，内置 **ADB + Scrcpy (SDL3 + D3D11VA)** 核心引擎，插上 Type-C 数据线即可享受最高 **120 FPS 原生硬件渲染 + 手机音频同步 + Windows RawInput 透明悬浮键鼠映射**。

---

## ✨ 核心特性

- **开箱即用 Windows 安装包（零外部依赖）**：
  - 原生 C# (.NET Framework 4.x) 桌面客户端，直接运行于 Windows 10 / 11，无需安装 Node.js、浏览器或手动下载 ADB/Scrcpy。
  - 安装包（`DroidDeck-Setup-1.0.0.exe`）内置完整 `adb.exe`、`scrcpy.exe`、`scrcpy-server` 及解码库，支持自定义安装目录、开始菜单/桌面快捷方式及控制面板标准卸载。
- **原生 120 FPS 硬件解码与桌面内嵌视口**：
  - 主画面直接驱动 Scrcpy 原生 **SDL3 + D3D11VA / FFmpeg** GPU 硬件解码并内嵌到 DroidDeck 控制窗口，同步支持低延迟 **手机音频转发**、最高 **24 Mbps** 码率与 **60 / 90 / 120 FPS** 帧率选择。
- **双通道分离与透明悬浮多指键鼠映射（Keymapper）**：
  - 采用 **「原生只读视频音频通道 + 独立 Scrcpy 二进制触控控制通道」** 架构，与原生 `scrcpy.exe` 解码互不干扰，通过 Win32 透明悬浮窗（`OverlayForm`）将键位标签直接叠加在画面上：
    - **Windows `WM_INPUT` (RawInput) 原始鼠标增量 + `ClipCursor`**：按 `` ` `` 键真正锁定指针在投屏视口安全区内，并自动无感回中，支持 **FPS 360° 无死角连续转身**，彻底摆脱浏览器 `PointerLock` 限制。
    - **`W / A / S / D` 8 方向平滑虚拟摇杆**：支持多指并发触控指针 ID，移动走位同时可触发技能/开火/跳跃。
    - **3 套开箱预设**：内置 **MOBA（王者荣耀 / LOL 手游）**、**FPS 射击（和平精英 / 暗区突围）**、**动作开放世界（原神 / 鸣潮 / 绝区零）**。
    - **🛠️ 键位可视化编辑**：直接在投屏画面上拖拽调整坐标、单击绑定键盘/鼠标按键、右键删除，配置保存在 `%LocalAppData%\DroidDeck\keymaps.json`。
- **实用设备控制与抗断流调优**：
  - 支持 **📱 手机熄屏投屏**（手机黑屏降发热，电脑端继续操作）、**📷 原画无损 PNG 截图**、系统导航键（返回 / 桌面 / 多任务 / 音量 / 电源）。
  - 针对 Windows 11 + ADB v37 常见的 `failed to get feature set` 与 `LIBUSB_ERROR_PIPE` 问题，内置 `ADB_LIBUSB=1` + `ADB_BURST_MODE=0` 强制环境变量、断流自动重连与一键 **🔄 重置 ADB**。

---

## 📦 安装与使用（普通用户）

1. 运行安装程序 `DroidDeck-Setup-1.0.0.exe`，按向导完成安装（约 10 MB，已内置全部组件）。
2. 使用支持数据传输的 Type-C 数据线连接手机与电脑（建议连接电脑主板后置原生 USB 3.x / Type-C 接口）。
3. 首次连接请在手机上开启 **USB 调试** 与 **USB 调试（安全设置）**（详见下方说明）。
4. 打开桌面或开始菜单中的 **DroidDeck**，等待左上角识别到设备后，点击 **▶ 启动原生投屏** 即可开始。

---

## 📱 手机端设置（以小米 / 澎湃 HyperOS 为例）

首次使用 Type-C 连接电脑时，在手机上完成以下 3 步（之后插线即连）：

1. **开启开发者选项**：打开手机 `设置 → 我的设备 → 全部参数与信息`，连续点击 7 次 **`OS 版本`**（其他安卓品牌连续点击 `版本号`）。
2. **开启 USB 调试**：进入 `设置 → 更多设置 → 开发者选项`，打开 **「USB 调试」**，在手机弹窗勾选「始终允许使用这台计算机进行调试」并确定。
3. **【关键】开启安全设置**：在开发者选项中继续往下滑，打开 **「USB 调试（安全设置）」**。
   - *说明：小米 / HyperOS 必须开启此选项，才允许电脑通过鼠标点击屏幕和执行游戏键鼠映射。*

---

## 🎮 操控模式说明

在左侧面板顶部可随时切换 3 种模式：

| 模式 | 说明 |
| :--- | :--- |
| **🖱️ 鼠标触控** | 普通鼠标点击、拖拽滑动、滚轮上下滚动日常操作手机 |
| **🎮 键鼠映射激活** | 启用 `WASD` 摇杆、键盘/鼠标技能键及 `` ` `` 键 RawInput 锁定鼠标 360° 准星视角 |
| **🛠️ 键位可视化编辑** | 在画面上拖拽调整各按键坐标，单击图标重新绑定按键，右键删除，点击保存生效 |

---

## 🛠️ 从源码编译桌面端与安装包（开发者）

### 构建依赖
- Windows 10 / 11（内置 .NET Framework 4.x 与 Visual Studio BuildTools / MSBuild Roslyn `csc.exe`）
- [Inno Setup 6](https://jrsoftware.org/isinfo.php)（可通过 `winget install JRSoftware.InnoSetup` 安装）
- `bin/scrcpy/` 目录下的 Scrcpy Windows x64 运行文件（可通过 `powershell -File scripts/setup-scrcpy.ps1` 自动下载）

### 一键编译 `.exe` 与安装包

```powershell
# 仅编译桌面客户端到 desktop/build/DroidDeck.exe
powershell -NoProfile -ExecutionPolicy Bypass -File desktop/build.ps1

# 编译桌面客户端并打包生成 dist/DroidDeck-Setup-1.0.0.exe 安装包
powershell -NoProfile -ExecutionPolicy Bypass -File build-installer.ps1
```

### 项目结构

```text
DroidDeck/
├── build-installer.ps1          # 一键编译桌面客户端并生成 Windows 安装包
├── desktop/                     # 原生 Windows C# 桌面客户端 (.NET Framework 4.x)
│   ├── Program.cs               # 单实例守护与 DPI 感知入口
│   ├── build.ps1                # Roslyn 编译脚本（零外部 NuGet 依赖）
│   ├── Core/
│   │   ├── AdbManager.cs        # 内置 ADB 探测、设备轮询、截图与抗干扰参数注入
│   │   ├── ScrcpyHost.cs        # 原生 SDL3/D3D11VA 窗口内嵌、120FPS 推流与自动重连
│   │   ├── ScrcpyControlChannel.cs # 独立 Scrcpy 二进制触控/按键/息屏控制通道
│   │   ├── InputEngine.cs       # WH_KEYBOARD_LL / WH_MOUSE_LL / RawInput 360° 视角引擎
│   │   └── NativeMethods.cs     # Win32 P/Invoke 封装
│   ├── Models/
│   │   └── KeymapModels.cs      # MOBA / FPS / ARPG 预设与 %LocalAppData% 持久化
│   └── UI/
│       ├── MainForm.cs          # 深色桌面主控台与嵌入式视频舞台
│       ├── OverlayForm.cs       # 分层透明悬浮键位图层
│       └── KeyBindDialog.cs     # 交互式按键捕获弹窗
├── installer/
│   └── DroidDeck.iss            # Inno Setup 6 安装包打包脚本
├── scripts/
│   └── setup-scrcpy.ps1         # 拉取 Scrcpy + ADB 核心组件到 bin/scrcpy/
├── server.js / public/          # （早期网页版实现，保留作协议参考）
└── .gitignore
```

---

## 🔧 常见断流 / 卡顿排查

1. **经常隔几十秒卡顿 2~3 秒后画面刷新，或偶发 `failed to get feature set`**：
   - **优先插主板后置原生 USB 3.x / Type-C 接口**：部分前置面板或 USB 2.0 分线器/扩展坞供电或信号不稳定，在持续高码率推流时容易触发物理断连重置。
   - **关闭 Windows USB 选择性暂停**：打开 `控制面板 → 电源选项 → 更改计划设置 → 更改高级电源设置 → USB 设置 → USB 选择性暂停设置`，将其设置为 **「已禁用」**。
   - **使用内置重置**：若出现 ADB 识别异常，可点击界面快捷栏的 **「🔄 重置ADB」** 按钮。
2. **能看到画面但鼠标点不动**：
   - 请确认手机开发者选项中的 **「USB 调试（安全设置）」** 已开启（小米/Redmi 机型必开）。

---

## 📄 开源协议与致谢

- 本项目基于 [MIT License](LICENSE) 开源。
- 投屏与底层控制协议基于开源项目 [Genymobile/scrcpy](https://github.com/Genymobile/scrcpy) 与 Android ADB。
