# TikTok on the Android Emulator: what lagged and what fixed it

Notes from tuning Androidzy's **TikTok (smooth video)** profile (Androidzy 1.3 - 1.4). Every number below was measured, on one PC, with the tools listed at the end. Where something was not measured, it says so.

**Test PC:** Intel Core i5-11300H (4 cores / 8 threads), NVIDIA GeForce RTX 3050 Laptop + Intel Iris Xe, 16 GB RAM, Windows 11, mains power. Android 14 (API 34) Google Play x86_64 image, Android Emulator 37.x, Pixel Fold screen (2208 x 1840) unless noted.

**Reference:** Google Play Games Developer Emulator (GPGDE) played TikTok smoothly on the same PC. Its stack: Android 14 x86_64, ANGLE over Vulkan on the RTX 3050, OpenGL ES 3.2, 1920 x 1080 at 60 Hz, and **only software video decoders** (`codec2::software`).

## Summary

| # | Problem | Evidence | Change | Solved? |
|---|---|---|---|---|
| 1 | Videos froze or crawled after every swipe | TikTok used `c2.goldfish.hevc.decoder`; logcat full of `Ignoring stale input buffer done callback` / `MediaCodec discarded an unknown buffer`; new videos rendered 1-80 frames in 43 s | `-feature -HardwareDecoder` | **Yes** for swipe freezes |
| 2 | Slow loading, "internet issues" | TCP round trip ~107-110 ms in the emulator vs 16 ms on the PC; ping up to 464 ms under load | `-feature -WiFiPacketStream` | **Yes**: ~24 ms, faster downloads |
| 3 | Emulator wasted power while idle, and had no headroom when busy | Idle home screen held ~18% of the PC with Android itself at ~0% | Adaptive governor + 6 cores | **Yes** for idle power (~4-5%); smoothness effect not isolated |
| 4 | Remaining judder and scroll jank | 17.5% of video frames one refresh late, 9.5% janky UI frames, many "slow issue draw commands" | Not fixed in the profile | **Partly open**, see below |

## 1. Video decoder: goldfish vs software

On this image the emulator exposes host-side "goldfish" decoders (`c2.goldfish.h264/hevc/vp8/vp9.decoder`, enabled by `ro.boot.qemu.hwcodec.*=2`) and lists them before Android's own `c2.android.*` decoders. TikTok picks them because they look like hardware decoders.

They break on TikTok's usage pattern: it flushes and swaps players constantly (preloading the next video, seeking, looping). In a scripted swipe test (8 swipes, 4 s apart) with goldfish on:

- every new video got its own video layer that showed only **1-80 frames in 43 s** (about 1-2 fps each),
- **76** video frame gaps of 100 ms or more,
- UI: 34% janky frames, 90th percentile 121 ms, 99th 500 ms.

With `-feature -HardwareDecoder` the `ro.boot.qemu.hwcodec.*` properties disappear and the goldfish decoders are not offered. Steady playback measured **28-30 fps** in both modes, so the difference is swipes, not steady playback.

A surprise: TikTok then does **not** use Android's `c2.android.hevc.decoder`. With no hardware decoder it falls back to ByteDance's own in-app software decoder (`ByteVC1_dec ... Bytedance bytevc1 ByteVC1 decoder ... is created` in logcat, no TikTok sessions in `dumpsys media.metrics`). GPGDE, which only has software decoders, behaves the same way. That fallback is CPU work inside TikTok (about 150-300% CPU), which is why cores and priority matter for this profile.

## 2. Network: the netsim Wi-Fi relay

By default the emulator's Wi-Fi goes through **netsim** (the packet streamer that simulates radios). Measured from inside Android with repeated TCP connects and timed downloads:

| | netsim Wi-Fi (default) | `-feature -WiFiPacketStream` | The PC itself |
|---|---|---|---|
| TCP round trip | 107-110 ms avg, 190 ms max | **24 ms** avg, 30 ms max | ~16 ms |
| Download (same CDN, 8 s window) | 14.9 Mbps | **16.4-22.6 Mbps** | 24.4 Mbps |
| ICMP ping while TikTok ran | 123-257 ms avg, up to 464 ms | (slirp does not answer ICMP on Windows) | 16 ms |

Wi-Fi stays connected and validated without the streamer, so apps still see an unmetered Wi-Fi network. TikTok makes many small requests while it preloads, so ~85 ms added to every round trip was felt as slow loading. In a later 91 s session there were **no network errors** in TikTok's log and downloads came in 17-39 Mbps bursts.

## 3. Power: busy vs idle

- With Android idle at ~0% CPU, the emulator process still used **~18% of the PC**: its own window redraw and graphics loop, not Android.
- The **governor** (`src/Governor.cs`) reads Android's own CPU counters (`/proc/stat`) every 1.5 s, not the emulator process, whose baseline never drops. Busy (12% or more of Android's cores): the emulator runs at above-normal priority and Windows power throttling is explicitly off. Quiet for 20 s: below-normal priority and Windows efficiency mode (EcoQoS).
- Measured: idle dropped to **~4-5% of the PC**, and the switch back to boost happens on the next poll after a tap or swipe. In a 91 s session of normal use it stayed in boost the whole time, with no flapping.
- **6 cores** (capped at all-but-two of the PC's threads): cores Android is not using halt, so they cost nothing when idle. Its effect on smoothness was not measured in isolation.

## 4. What is left

One 91 s session of normal use (scrolling and watching, unfolded 2208 x 1840, all fixes above on):

| Video frame gaps | Share |
|---|---|
| 34 ms or less (smooth) | 81.9% |
| 35-66 ms (one refresh late) | 17.5% |
| 67-99 ms | 0.4% |
| 100 ms or more (visible) | 0.2% (3 freezes of 0.3-1 s) |

UI: 9.45% janky frames, 50th / 90th / 99th percentile 18 / 31 / 133 ms, 160 "slow issue draw commands". Android CPU was 25-54%, so the CPU was not saturated.

Reading:

- Part of the 35-66 ms bucket is normal cadence: 24/25 fps videos on a 60 Hz display show some frames for an extra refresh, on real phones too.
- The rest, and the "slow issue draw commands", come from the graphics path: each frame TikTok's software decoder produces is uploaded through the emulator's OpenGL pipe, and composition and TikTok's UI are drawn at the screen's full resolution. All of that scales with pixels, and the Pixel Fold's inner screen is 4.1 MP per frame, about twice GPGDE's 1920 x 1080 (2.1 MP).
- **Not measured yet:** folding the Pixel Fold switches Android to the 1104 x 1840 cover region (`wm size` reports `1104x1840`, about 2.0 MP), which should roughly halve that work. The session meant to measure it was cut short. A 1080 x 1920 portrait screen would be the equivalent without a fold.

## Ruled out

- **IPv6:** the emulator hands out only site-local `fec0::` addresses, so Android does not treat IPv6 as working and real traffic stays on IPv4.
- **Background work after boot:** no `dex2oat`, no Play Store downloads, no running jobs during the slow sessions.
- **Wrong GPU or power state:** SurfaceFlinger reports the RTX 3050; the laptop was on mains power.
- **The emulator's graphics feature flags:** asg transport, YUV cache, host composition, GL DMA and direct memory are already on in this image.

## Notes on measuring

- Scripted A/B runs on cold-booted throwaway copies (`-read-only`) were noisy: TikTok's first minutes after a cold boot are slow, and one run hit an ANR dialog. The swipe test and the user-driven sessions were the reliable signals.
- Monitoring that runs `dumpsys` every few seconds adds load inside Android and keeps the governor in boost, so it hides the very behaviour you want to see. The final measurements used a passive recorder: `/proc/stat` and `/proc/net/dev` every 3 s, one SurfaceFlinger and gfxinfo dump at the end.

Tools: `dumpsys SurfaceFlinger --timestats` (per-layer frame counts and present-to-present histograms), `dumpsys gfxinfo <package>` (UI jank), `dumpsys media.metrics` (decoder sessions), `logcat` (decoder and network errors), `/proc/stat` and `/proc/net/dev`, and for the network tests `nc` inside Android against a CDN.
