# Third-Party Notices

This file documents the primary third-party components referenced by BattleLuck at build time and runtime.

BattleLuck itself is licensed under MIT. See `LICENSE`.

## NuGet Package Dependencies

| Component | Version | Role | License | Source |
| --- | --- | --- | --- | --- |
| `VAutomationCore` | `1.0.2` | Shared automation/gameplay support library | MIT (embedded package `LICENSE` file) | https://github.com/Coyoteq1 |
| `VRising.VampireCommandFramework` | `0.10.4` | Command registration and parsing | MIT | https://github.com/decaprime/VampireCommandFramework |
| `VampireReferenceAssemblies` | `1.1.11-r96495-b8` | Metadata-only V Rising reference assemblies for compilation | License not declared in NuGet metadata; see project repository | https://github.com/mfoltz/VampireReferenceAssemblies |
| `Il2CppInterop.Runtime` | `1.4.6-ci.426` | IL2CPP/.NET interop runtime | LGPL-3.0-only | https://github.com/BepInEx/Il2CppInterop |
| `HookDOTS.API` | `1.1.1` | ECS hook API | GPL-3.0-only | https://github.com/cheesasaurus/HookDOTS |
| `System.Text.Json` | `6.0.11` | JSON serialization | MIT | https://github.com/dotnet/runtime |

## Runtime Components From The BepInEx / IL2CPP Stack

These components are not declared as direct `PackageReference` items in `BattleLuck.csproj`, but they are part of the runtime/tooling chain used by the current local server/BepInEx installation and referenced by the project.

| Component | Version | Role | License | Source |
| --- | --- | --- | --- | --- |
| `BepInEx.Core` | `6.0.0-be.733` | Core mod loader/runtime APIs | LGPL-2.1-only | https://github.com/BepInEx/BepInEx |
| `BepInEx.Unity.IL2CPP` | `6.0.0-be.733` | Unity IL2CPP integration | LGPL-2.1-only | https://github.com/BepInEx/BepInEx |
| `HarmonyX` (`0Harmony.dll`) | `2.10.2` | Runtime patching library behind the local `0Harmony.dll` | MIT (embedded package `LICENSE` file) | https://github.com/BepInEx/HarmonyX |
| `Iced` | `1.21.0` | Disassembler utility used by the BepInEx/IL2CPP toolchain | MIT | https://github.com/icedland/iced |
| `MonoMod.RuntimeDetour` | `22.7.31.1` | Runtime detouring support in the BepInEx stack | MIT | https://github.com/MonoMod |

## Game-Provided Assemblies

BattleLuck also compiles against and/or loads assemblies that come from the user's own V Rising server installation, including `Stunlock.Core.dll`, Unity libraries, and other VRising-managed assemblies. Those binaries are not distributed by this repository and remain subject to the V Rising / Stunlock licensing terms. Users must obtain them from a legitimate game or dedicated-server installation.

## Notes

- License data above was taken from the currently resolved package metadata in the local NuGet cache and, where required, the upstream repository license files.
- `VampireReferenceAssemblies` currently exposes project/repository URLs in NuGet metadata, but no SPDX license expression or embedded license file was available from the resolved package metadata during generation of this notice file.
- If package versions change, this file should be reviewed and updated alongside `Directory.Packages.props` and any server-runtime dependency changes.