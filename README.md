# Android emulator for apps: a BlueStacks, MEmu & LDPlayer alternative (Androidzy)

**Androidzy by [@dznye](https://github.com/dznye)** - a free, open-source Android emulator for Windows PC, built for running **apps** (not games). A simpler alternative to BlueStacks, LDPlayer, MEmu and MuMu Player if all you want is Android apps on your desktop: a second Android device with real Google Play, the official Google emulator and one-click GPU profiles.

Website: **https://dznye.github.io/Androidzy/**

![Androidzy](docs/screenshot.png)

## Why

Most Android emulators are marketed for games. Androidzy is for everything else you do on a phone, on the computer you already sit at all day:

- a **second phone for work** - Slack, Teams, Gmail, Telegram, kept apart from your personal phone
- **accounts you own or manage** - sign in to as many accounts as each app allows
- **mobile-only apps on a big screen**, with keyboard, mouse, screenshots and recording
- **testing apps** on real Android 14 with adb, without installing Android Studio

Windows Subsystem for Android ended on March 5, 2025, so there is no built-in way to run Android apps on Windows 11 any more. Androidzy fills that gap.

It also fixes a real annoyance: on laptops with two GPUs (Intel + NVIDIA/AMD) the emulator can render on the wrong one, or even run OpenGL on one GPU and Vulkan on the other. Androidzy pins both to the GPU you choose, sets up a sensible virtual device, and gets out of the way.

**Responsible use:** for accounts you own or are authorised to manage. Androidzy does not hide that it is an emulator, and some apps (many banking and DRM-protected streaming apps) block emulators. It currently runs one Android device at a time.

## Features

- **Starts clean** - on a new device the preinstalled apps are removed automatically (Play Store, Files, Settings and the Google search app stay). Reversible from Google Play; switch it off with *Remove preinstalled apps*.
- **Desktop share folder** - `Desktop\Androidzy Share` is created automatically. Anything dropped in it while the emulator runs is copied to the phone (photos to Pictures, videos to Movies, music to Music, the rest to Download, under `Androidzy/`) and registered with Android's media library.
- **US location on every boot** - GPS is set to New York each time Android starts.

- **GPU profiles** - Dedicated, Integrated, Windows default, or Software. OpenGL *and* Vulkan follow the same GPU; the status line shows which one is in use.
- **Fast restarts** - quick-boot snapshots bring Android back in about a second.
- **Zero setup** - uses the Android SDK you already have from Android Studio, or downloads what it needs from Google on first run.
- **One small exe** - a single native Windows program (about 60 KB) built with the C# compiler that ships with Windows. No installer, no runtime to install.
- Resolution presets, CPU/memory settings, cold boot, factory reset, per-launch logs.

## Requirements

- Windows 10/11 x64
- Hardware virtualization enabled in the BIOS and the Windows feature **Windows Hypervisor Platform**
- About 6 GB of free disk space (more if you keep a quick-boot snapshot)

## Getting started

1. **[Download Androidzy.exe](https://github.com/dznye/Androidzy/releases/latest/download/Androidzy.exe)** (one 72 KB file; [release notes and SHA-256](https://github.com/dznye/Androidzy/releases/latest)), or build it yourself (below). Windows SmartScreen may warn because the exe is not code-signed yet.
2. Run `Androidzy.exe`.
   - If an Android SDK with the Android 14 *Google Play* system image is found (Android Studio's default location, `ANDROID_HOME` or `ANDROID_SDK_ROOT`), it is used as is.
   - Otherwise a **First-run setup** panel offers to download the emulator, platform tools and system image (about 2 GB) from Google, after you accept Google's license.
3. Pick a GPU profile and press **Launch**.

Settings, the virtual device and any downloaded components live in `%LOCALAPPDATA%\Androidzy`. Put a `sdk` folder next to `Androidzy.exe` and it runs as a portable install instead.

## GPU profiles

| Profile | What it does |
|---|---|
| **Dedicated GPU** | Tells Windows to run the emulator on the high-performance GPU and pins Vulkan to the same one. Default on machines with an NVIDIA/AMD card. |
| **Integrated GPU** | Same, for the power-saving GPU. Cooler and quieter, slower. |
| **Windows default** | Removes the override; Windows and the emulator choose. |
| **Software renderer** | SwiftShader/ANGLE on the CPU. Slow; only for broken GPU drivers. |

The override is the same per-app setting as *Settings > System > Display > Graphics*, written under `HKCU\Software\Microsoft\DirectX\UserGpuPreferences` for the emulator executables only. Choose **Windows default** to remove it.

## Command line

```
Androidzy.exe [--launch] [--profile dedicated|integrated|auto|software]
              [--cold] [--no-save] [--headless] [--verbose]
              [--accept-license] [--own-copy]
```

`--accept-license` starts the first-run download without clicking (you are accepting Google's license by using it). `--own-copy` ignores an existing SDK and downloads a private copy.

## How it works

Androidzy never modifies or bundles the emulator. It writes the AVD config, sets the Windows GPU preference for the emulator's executables, and starts `emulator.exe` with:

- `-gpu host` (or `swangle` for the software profile),
- `ANDROID_EMU_VK_SELECT_GPU=0` - the Windows preference orders the host GPUs, so index 0 is the pinned GPU. Without it the emulator scores GPUs on its own and can pick a different one for Vulkan than for OpenGL,
- `ANDROID_EMULATOR_WAIT_TIME_BEFORE_KILL=60` so saving a snapshot is not cut short,
- `-no-metrics`, `-accel on`, and the usual boot and network flags.

## Building

Needs only Windows (the C# compiler is part of the .NET Framework that ships with it):

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

The result is `bin\Androidzy.exe`.

## Legal

- Androidzy's own code is released under the [MIT License](LICENSE).
- **No Google software is included in this repository or in Androidzy's releases.** The Android Emulator, platform tools and system image are downloaded from Google's servers (or taken from your existing SDK) and are licensed to you by Google under the [Android Software Development Kit License Agreement](https://developer.android.com/studio/terms). Please read it; it is between you and Google.
- Androidzy is an independent project. It is not affiliated with, sponsored by or endorsed by Google. Android, Google Play and the Android robot are trademarks of Google LLC.
