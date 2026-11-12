# Third-party notices

LegionBar source code and original project artwork: Copyright (C) 2026 Ktclat and contributors, AGPL-3.0-only. See LICENSE.

## Runtime and dependencies

- .NET / Windows Forms: Microsoft and .NET contributors, MIT. https://github.com/dotnet/runtime/blob/main/LICENSE.TXT and https://github.com/dotnet/winforms/blob/main/LICENSE.TXT
- System.Management 9.0.10 and transitive System.CodeDom 9.0.10: .NET contributors, MIT. https://github.com/dotnet/runtime/blob/v9.0.10/LICENSE.TXT
- Windows APIs are supplied by the operating system. NVIDIA NVML is loaded from the installed NVIDIA driver; this repository does not distribute NVIDIA binaries or grant rights to them.

The default portable build relies on a separately installed .NET Desktop Runtime. The build script includes the upstream .NET MIT license and runtime third-party notice in the output directory; self-contained distributions must retain the notices shipped with their .NET components. Refer to packages.lock.json for the resolved dependency graph.

## References

- LenovoLegionToolkit: https://github.com/BartoszCichecki/LenovoLegionToolkit (GPL-3.0). Its sensor API descriptions informed the Lenovo read-only method names and identifiers used here. No LenovoLegionToolkit code, executable, driver or icon is bundled. LenovoSensors.cs implements a separate System.Management client.
- TrayS: https://github.com/cgbsmy/TrayS, an interface inspiration for a compact two-row taskbar monitor. No TrayS code, binaries or artwork is bundled.

GNU AGPL-3.0 license text: Free Software Foundation, Inc. The LICENSE file reproduces the unmodified license text; its own copyright notice and copying terms remain in force.

All third-party names and trademarks belong to their respective owners. No sponsorship or endorsement is implied. See docs/ICON-DISCLAIMER.md.
