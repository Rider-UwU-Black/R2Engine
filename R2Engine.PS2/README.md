# R2Engine PS2 Backend Probe

## Final ISO export

In the editor's Build menu, choose **Build to PS2 (Final)**. It saves the current
scene, runs the normal project cook, builds a separate disc ELF, and exports:

`<BuildOutput>/PS2-Final/R2GM_000.01.<StartupScene>.iso`

The adjacent `asset-map.json` lists the packaged files and SHA-256 hashes. Every
file is extracted from the finished ISO and hash-checked before publication.
The ISO includes the boot ELF, SYSTEM.CNF, audio service, cooked scenes, meshes,
animation, textures/UI sprites, and exported Assets/Audio files. It does not
include the .NET editor/player or original authoring files. Existing native PS2
feature limitations still apply; "Final" means self-contained disc packaging.

The disc build resolves logical asset names through PATHS.BIN to flat ISO9660
filenames. HostFS and disc builds use separate object files and executables;
**Build & Run PS2 (PCSX2)** remains the development workflow.

Command-line export after cooking:

```powershell
.\R2Engine.PS2\build-final.ps1 -CookedBuildDirectory .\Builds\Windows -SceneName DemoScene
```

Requires a separate Ubuntu WSL PS2 toolchain, Python 3, and `genisoimage` (including
`isoinfo`). The ISO creator does not install dependencies automatically.

Test the ISO by opening it directly as a disc image in PCSX2, without an ELF
override. Initial disc boot/rendering has been verified in PCSX2; physical PS2
operation and loader compatibility have not yet been verified. Video is NTSC.
A stock PS2 does not boot an unsigned homebrew disc: use an appropriate homebrew
loader/modification. Consult your loader's instructions for its ISO location and
supported media. Do not assume burning the ISO alone will make it bootable on an
unmodified console. Back up your memory card before testing saves on hardware.

The sections below describe the historical bring-up/probe workflow.

This is the first native PlayStation 2 target. It deliberately does not reference
.NET, Silk.NET, OpenGL, OpenAL, or the editor. PS2SDK builds C/C++ for the Emotion
Engine; the portable C# runtime remains the behavioral reference while PS2 runtime
systems are implemented natively behind equivalent platform boundaries.

## Current proof

- Initializes the GS through gsKit.
- Initializes GIF DMA through dmaKit.
- Uses a 24-bit color framebuffer and 16-bit depth format.
- Queues and presents 240 animated colored sprites plus a heartbeat marker per frame.
- Polls controller port 1 through PS2SDK libpad.
- Draws a built-in 3x5 bitmap HUD with measured FPS and pad connection state.
- Negotiates and locks DualShock analog mode after the controller connects.
- Shows `PAD:A` for analog mode and `PAD:D` for digital mode.
- Visualizes the left stick with a blue reticle and the right stick with a green reticle; button activity turns both pink.
- Parses every editor R2TX v1 format: RGBA32, A1B5G5R5 RGBA16, indexed-8,
  and packed indexed-4 textures with PS2 CSM1 palette handling.
- Submits a bounded 12-triangle cube with CPU rotation, perspective projection,
  back-face rejection, depth values, perspective-correct STQ texture coordinates,
  and the loaded R2TX texture.
- Parses the editor's R2MS v1 cooked-mesh layout with bounds checking, 32-byte
  position/normal/UV vertices, and PS2-friendly 16-bit triangle indices.
- Parses R2SC v1 cooked scenes with compact asset tables and transformed mesh instances.
- Applies ambient plus directional vertex lighting from cooked normals, multiplied
  by each scene instance's material tint.
- Loads cooked scene data through PS2SDK `host:` file I/O in PCSX2, with a bounded
  one-megabyte read and an embedded fallback for unavailable host storage.
  PCSX2 resolves this device relative to the launched ELF, so probe assets are staged
  under `bin/assets` by the launcher.
- Resolves the scene's relative mesh and texture table entries and loads the complete
  R2SC -> R2MS/R2TX dependency chain without embedding those assets in the successful path.
- Produces `bin/r2engine-ps2-probe.elf` when the toolchain is available.

This intentionally starts below the full scene renderer. It proves compiler, ELF
linking, GS setup, DMA submission, frame presentation, controller input, and the
first cooked-texture asset path before static mesh submission is introduced.

The probe has been visually validated in PCSX2: the animated 20x12 grid and cyan
heartbeat, controller input HUD, and an R2TX-loaded RGBA32 checker texture render
and present correctly without a crash. The rotating cube also validates exterior-face
culling, depth-tested textured triangles, and perspective-correct STQ mapping.
The same cube remains visually correct when sourced through the bounds-checked R2MS
package loader rather than renderer-owned vertex arrays.
External R2SC loading is also validated in PCSX2 through the ELF-relative HostFS root.
Cooked normals now drive validated directional vertex lighting, and per-instance
material tint from R2SC is visibly applied.
R2SC v2 adds the editor scene's primary camera and the native path now applies full
XYZ instance transforms. R2SC v1 remains supported for the embedded fallback.
R2SC v3 adds the scene background, ambient color, and first directional light so the
native frame no longer depends on probe-owned environment or lighting values.
R2SC v4 carries linear fog settings. Fog is optional and uses a depth-interpolated
GS alpha overlay, trading an additional triangle pass for fixed-function compatibility.
R2SC v5 adds per-instance built-in material flags, including Lit/Unlit, double-sided,
and fog reception. R2TX v2 preserves nearest versus bilinear texture filtering.

## Required toolchain

Install the ps2dev environment containing PS2Toolchain, PS2SDK, gsKit, and ps2client.
Set absolute paths without spaces:

```text
PS2DEV=<ps2dev installation>
PS2SDK=<PS2DEV>/ps2sdk
GSKIT=<PS2DEV>/gsKit
```

Add the PS2DEV `bin`, `ee/bin`, `iop/bin`, `dvp/bin`, and PS2SDK `bin` directories to
`PATH`. The required EE compiler is `mips64r5900el-ps2-elf-gcc`.

The official [ps2dev environment](https://github.com/ps2dev/ps2dev) publishes prebuilt
toolchains and documents the required environment variables. The native runtime uses
[PS2SDK](https://github.com/ps2dev/ps2sdk) and
[gsKit](https://github.com/ps2dev/gsKit). WSL/Linux or the official
`ps2dev/ps2dev` container are likely easier to automate from a modern 64-bit Windows
host than the available Windows x86 package.

## Build

The project wrapper requires an initialized Ubuntu distribution under WSL. The PS2DEV
toolchain must be installed at `~/.local/ps2dev` inside Ubuntu. Run
`setup-toolchain.ps1` once after Ubuntu is ready to install the Linux packages and toolchain.

From PowerShell:

```powershell
./build-ps2.ps1
```

Build and launch the probe in the configured PCSX2 installation:

```powershell
./run-pcsx2.ps1
```

After building a project in the editor, stage and launch one of its cooked scenes:

```powershell
./run-pcsx2.ps1 -CookedBuildDirectory ../Builds/Windows -SceneName DemoScene
```

This copies the selected R2SC package plus its cooked R2MS/R2TX trees into the
ELF-relative HostFS directory. Omitting these arguments continues to run the small
self-contained probe scene.

The current bounded scene runtime loads up to 16 distinct mesh packages and 16
distinct texture packages, resolves them per instance, and supports untextured
vertex-lit instances. These limits keep native memory and VRAM use explicit while
the backend is being validated.

The current default emulator path is `E:\PCSX2\pcsx2-qt.exe`. Use
`-Pcsx2Path` if that installation moves, or `-SkipBuild` to launch the existing ELF.

Or from a compatible shell:

```text
make
```

With a network-connected console running PS2Link, `make run` sends the ELF through
`ps2client`. Emulator and real-hardware validation are both required before this
milestone is considered complete.

## Installed development environment

The setup helper installs the official v2.0.0 Linux toolchain at
`~/.local/ps2dev`. Re-run `install-toolchain-wsl.sh` to repair or reproduce that
installation. The generated probe is a 32-bit little-endian MIPS executable.

## Next proof steps

1. Define the cooked binary scene header shared with the Windows asset cooker.
2. Add cooked mip-level selection and texture residency management.
3. Load editor-cooked R2SC, R2MS, and R2TX files through the PS2 filesystem layer.
4. Measure RAM, VRAM, queue usage, and frame time on real hardware.
