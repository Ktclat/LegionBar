# LegionBar

[![Build](https://github.com/Ktclat/LegionBar/actions/workflows/build.yml/badge.svg)](https://github.com/Ktclat/LegionBar/actions/workflows/build.yml)
[![License: AGPL-3.0](https://img.shields.io/badge/license-AGPL--3.0-blue)](LICENSE)

Windows 10/11 x64 简约任务栏硬件监控，针对联想拯救者的温度和风扇接口适配。原生 WinForms + Windows API，无浏览器运行时。

## 功能

- CPU 占用与温度、NVIDIA GPU 占用与温度、内存占用、风扇转速、上传 / 下载速度。
- 透明背景透出任务栏自身材质，文字自动跟随 Windows 任务栏深浅色。
- 默认简约模式：紧凑双行数值，隐藏 CPU/GPU/RAM/FAN 标签和分隔线；可切换完整模式。
- 仅托盘图标右键打开设置菜单，信息面板右键及双击都不打开窗口；面板可解锁拖动，不再保留“查看详情”窗口。
- 自动检测显示器，支持仅主屏、所有显示器或逐屏勾选；接入、断开显示器后自动更新，手选屏幕暂时断开时回退主屏，重新接入后恢复选择。
- 多屏共享一个硬件采样器与一个托盘图标，每个面板独立保存水平位置，并按所在屏幕的任务栏、全屏状态及 DPI 分别显示。
- 字体优先使用 Windows 11 的 Segoe UI Variable Text，缺少时回退 Segoe UI；两行数值采用相同字体、字号、常规字重和前景色（高温仍使用提示色）。
- 主菜单与子菜单适配深浅色。菜单向右上方展开并限制在同一屏幕，点击外部、切换应用或 Esc 时关闭。
- 跟随主任务栏显示 / 隐藏，主屏有可见全屏窗口时隐藏，即使焦点移到副屏也保持隐藏；支持网络接口选择、刷新间隔和当前用户开机启动。
- 黑白原创图标。程序不发起网络请求，不自动更新，不安装驱动，不修改风扇或性能模式。

## 运行

运行构建产物中的 `LegionBar.exe`。默认便携构建依赖 **.NET 9 Desktop Runtime x64**，也可以构建包含运行时的版本。

**CPU 温度和风扇转速需要管理员权限。** 对程序选择“以管理员身份运行”，或在托盘菜单选择“启用温度和风扇（管理员）”，由用户在 Windows UAC 中授权。普通权限读不到时显示 `--`，其余指标仍可使用。当前用户开机启动默认关闭，开启后以普通权限运行；它不会绕过 UAC。

右键托盘图标 → **外观 → 简约显示** 可切换布局。默认在内存下方显示 CPU 风扇 RPM，可切换 GPU 风扇或两者较高转速。网速 K/s、M/s、G/s 表示 KiB/s、MiB/s、GiB/s。

右键托盘图标 → **显示器** 可选“自动 · 仅主屏”“所有显示器”或勾选一块 / 多块屏幕。副屏必须在 Windows 中启用该屏任务栏；若菜单标注“无任务栏”，面板不会悬浮到该屏桌面上。默认仍仅主屏，不自动增加面板。

配置和错误日志存放于 `%LOCALAPPDATA%\LegionBar`。退出后可直接删除便携程序；如启用过开机启动，请先取消相应菜单项。

## 构建

需要 Windows x64、.NET 9 SDK 和 PowerShell。

```powershell
# 构建便携单文件，并生成 ZIP / SHA-256
.\build.ps1

# 包含 .NET 运行时的版本
.\build.ps1 -SelfContained

# 可选：重新生成黑白 ICO / PNG
.\scripts\Generate-Icons.ps1
```

输出位于 `artifacts/portable`。图标 SVG、PNG、ICO 均在 `assets` 中，生成脚本随源码提供。

```powershell
# 两次实时采样和基本范围检查
.\artifacts\portable\LegionBar.exe --self-test --output .\diagnostics.json

# 管理员模式下额外要求 CPU 温度和两只风扇读数有效
.\artifacts\portable\LegionBar.exe --self-test --require-sensors --output .\diagnostics.json

# 生成深浅色菜单、子菜单、两种布局的验证图和定位报告
.\artifacts\portable\LegionBar.exe --verify-menu
```

硬件自检不适用于没有对应设备和权限的 CI 环境；GitHub Actions 验证还原、编译、打包，并使用合成窗口几何场景回归检查多屏全屏与任务栏显隐规则，不宣称验证温度或真实任务栏交互。

`--self-test-layout` 可单独运行上述布局规则检查，不读取硬件或操作实际窗口。全屏检测按主屏窗口的实际显示顺序和遮挡情况判断：普通最大化窗口不会被视为全屏，完全被其他普通窗口遮住的后台全屏窗口也不会误触发隐藏。

## 数据来源与范围

菜单缓存、统一桌面快照、异步网卡目录和最新采样合并等实现说明见 [架构与性能](docs/ARCHITECTURE.md)。`--benchmark-ui` 可在本机执行短时双屏性能采样与菜单缓存检查，生成 `performance-report.json`；此诊断模式不修改保存的配置，正常运行不会收集该报告。

- CPU：Windows Processor Utility，失败时回退 GetSystemTimes。采样窗口和任务管理器可能不同。
- GPU：NVIDIA 驱动 NVML，显示第一块 NVIDIA GPU；不支持通用 AMD/Intel GPU 遥测。
- CPU 温度：`LENOVO_FAN_METHOD.Fan_GetCurrentSensorTemperature`，CPU 传感器 ID 3；备用 `LENOVO_GAMEZONE_DATA.GetCPUTemp`。
- 风扇：`Fan_GetCurrentFanSpeed`，CPU / GPU FanID 为 0 / 1。仅调用读取方法，0 RPM 可表示停转。
- 内存：Windows 物理内存使用量。
- 网络：活动以太网 / Wi-Fi 累计字节差除以实际采样时间，支持手动选卡。自动模式根据接口类型及名称排除常见虚拟接口，不保证覆盖所有虚拟网卡。

已在 Ryzen 7 5800H / RTX 3050 Laptop 的 Legion 82JW 上验证管理员模式温度及风扇读取。不同年代的 Legion 可能需要其他传感器 ID 或接口。GPU 轮询可能影响独显休眠。

面板覆盖在所选显示器的横向任务栏上，不预留任务栏空间。遇到天气或按钮重叠，可解锁后逐屏拖动或减少显示项。自动隐藏、混合屏幕缩放和第三方任务栏工具仍可能存在兼容性差异。多屏已在上下排列的两块 1920×1080、125% 缩放显示器上验证；不同 DPI 的混合缩放仍需更多实机覆盖。

## 许可证与声明

代码及仓库内原创图标采用 **GNU AGPL v3.0 only**（SPDX: `AGPL-3.0-only`），完整文本见 [LICENSE](LICENSE)。

LegionBar 为独立的非官方项目，与 Lenovo / 联想、Legion / 拯救者、Microsoft、NVIDIA 无隶属、授权、合作或背书关系。黑白图标是本项目独立绘制的抽象监控波形，不使用联想或 Legion 官方商标 / 标志，也不表示获得官方认证。商标权不因代码许可证而获得授权。详见 [图标与商标声明](docs/ICON-DISCLAIMER.md) 和 [第三方说明](THIRD_PARTY_NOTICES.md)。

软件按许可证“现状”提供，不保证传感器读数、机型兼容性或任务栏行为；不应作为唯一的过热保护或设备安全机制。
