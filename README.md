# LegionBar

[![Build](https://github.com/Ktclat/LegionBar/actions/workflows/build.yml/badge.svg)](https://github.com/Ktclat/LegionBar/actions/workflows/build.yml)
[![License: AGPL-3.0](https://img.shields.io/badge/license-AGPL--3.0-blue)](LICENSE)

Windows 10/11 x64 简约任务栏硬件监控，针对联想拯救者的温度和风扇接口适配。原生 WinForms + Windows API，无浏览器运行时。

## 功能

- CPU 占用与温度、NVIDIA GPU 占用与温度、内存占用、风扇转速、上传 / 下载速度。
- 透明背景透出任务栏自身材质，文字自动跟随 Windows 任务栏深浅色。
- 默认简约模式：紧凑双行数值，隐藏 CPU/GPU/RAM/FAN 标签和分隔线；可切换完整模式。
- 仅托盘图标右键打开设置菜单，信息面板右键不弹菜单；面板可解锁拖动，双击查看快照。
- 主菜单与子菜单适配深浅色。菜单向右上方展开并限制在同一屏幕，点击外部、切换应用或 Esc 时关闭。
- 跟随主任务栏显示 / 隐藏，全屏时隐藏；支持网络接口选择、刷新间隔和当前用户开机启动。
- 黑白原创图标。程序不发起网络请求，不自动更新，不安装驱动，不修改风扇或性能模式。

## 运行

运行构建产物中的 `LegionBar.exe`。默认便携构建依赖 **.NET 9 Desktop Runtime x64**，也可以构建包含运行时的版本。

**CPU 温度和风扇转速需要管理员权限。** 对程序选择“以管理员身份运行”，或在托盘菜单选择“启用温度和风扇（管理员）”，由用户在 Windows UAC 中授权。普通权限读不到时显示 `--`，其余指标仍可使用。当前用户开机启动默认关闭，开启后以普通权限运行；它不会绕过 UAC。

右键托盘图标 → **外观 → 简约显示** 可切换布局。默认在内存下方显示 CPU 风扇 RPM，可切换 GPU 风扇或两者较高转速。网速 K/s、M/s、G/s 表示 KiB/s、MiB/s、GiB/s。

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

硬件自检不适用于没有对应设备和权限的 CI 环境；GitHub Actions 仅验证还原、编译、打包，不宣称验证温度或真实任务栏交互。

## 数据来源与范围

- CPU：Windows Processor Utility，失败时回退 GetSystemTimes。采样窗口和任务管理器可能不同。
- GPU：NVIDIA 驱动 NVML，显示第一块 NVIDIA GPU；不支持通用 AMD/Intel GPU 遥测。
- CPU 温度：`LENOVO_FAN_METHOD.Fan_GetCurrentSensorTemperature`，CPU 传感器 ID 3；备用 `LENOVO_GAMEZONE_DATA.GetCPUTemp`。
- 风扇：`Fan_GetCurrentFanSpeed`，CPU / GPU FanID 为 0 / 1。仅调用读取方法，0 RPM 可表示停转。
- 内存：Windows 物理内存使用量。
- 网络：活动以太网 / Wi-Fi 累计字节差除以实际采样时间，支持手动选卡。自动模式根据接口类型及名称排除常见虚拟接口，不保证覆盖所有虚拟网卡。

已在 Ryzen 7 5800H / RTX 3050 Laptop 的 Legion 82JW 上验证管理员模式温度及风扇读取。不同年代的 Legion 可能需要其他传感器 ID 或接口。GPU 轮询可能影响独显休眠。

面板覆盖在主显示器的横向任务栏上，不预留任务栏空间。遇到天气或按钮重叠，可解锁后拖动或减少显示项。自动隐藏、屏幕缩放、第三方任务栏工具和多屏切换仍可能存在兼容性差异。

## 许可证与声明

代码及仓库内原创图标采用 **GNU AGPL v3.0 only**（SPDX: `AGPL-3.0-only`），完整文本见 [LICENSE](LICENSE)。

LegionBar 为独立的非官方项目，与 Lenovo / 联想、Legion / 拯救者、Microsoft、NVIDIA 无隶属、授权、合作或背书关系。黑白图标是本项目独立绘制的抽象监控波形，不使用联想或 Legion 官方商标 / 标志，也不表示获得官方认证。商标权不因代码许可证而获得授权。详见 [图标与商标声明](docs/ICON-DISCLAIMER.md) 和 [第三方说明](THIRD_PARTY_NOTICES.md)。

软件按许可证“现状”提供，不保证传感器读数、机型兼容性或任务栏行为；不应作为唯一的过热保护或设备安全机制。
