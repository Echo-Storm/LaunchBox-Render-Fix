# LaunchBox GPU-to-CPU Fix

A tiny LaunchBox/Big Box plugin that stops the launcher from sustaining heavy GPU load while
you're just sitting in the game list — no videos, no screensaver, nothing playing. It moves that
rendering cost onto the CPU instead, where it's cheap.

## The problem

LaunchBox and Big Box are built on WPF (`net9.0-windows` as of LaunchBox 13.x). WPF hardware-
accelerates its UI by default: every bit of box art, the platform wheel, hover highlights,
background art, and scroll animations gets composited through the GPU, continuously, for as
long as the window is visible — whether or not anything is actually changing on screen.

On a normal-sized library this is easy to miss. It became impossible to miss on a **~15,600-game
library** (36 platforms, ~16k ROMs): LaunchBox was sitting at **50-55% GPU utilization** just
idling in a game list, with video snaps/previews fully disabled and nothing playing. That's not
a leak or malware, it's WPF's compositor doing exactly what it's designed to do — the difference
is that a bigger library means a bigger/more complex visual tree to keep composited every frame,
so the baseline cost is higher to begin with. Task Manager's Processes tab (GPU column) will show
this as `LaunchBox.exe`/`BigBox.exe` sitting well above 0% at idle.

This isn't a targeted performance problem (nothing feels slow) — it's wasted GPU headroom and
heat for an app that's supposed to be a static list you glance at before launching a game.

## What this plugin actually does

It uses LaunchBox's official [Plugin SDK](https://pluginapi.launchbox-app.com/) —
specifically `ISystemEventsPlugin.OnEventRaised`, which fires once the plugin is loaded
(`PluginInitialized`), and again once startup finishes (`LaunchBoxStartupCompleted` /
`BigBoxStartupCompleted`). Plugins run **in-process**, so this is just two public WPF API calls
made as early as possible in that process's lifetime:

1. **`RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly`**
   Forces WPF's compositor to rasterize entirely on the CPU for this process, instead of
   handing composited surfaces to the GPU. This is a real, documented WPF setting (used
   elsewhere for remote desktop / GPU-passthrough compatibility) — not a hack, not a registry
   tweak, not touching drivers.

2. **`Timeline.DesiredFrameRateProperty` default overridden to 30fps**
   WPF has exactly **one** render/composition thread per process, whether it's running hardware
   or software rendering — there's no multi-core option for the software rasterizer. So once
   step 1 moves all that work onto a single CPU core, capping how often animations (hover glow,
   fades, scroll momentum) get re-ticked meaningfully cuts how much work that one core has to do
   per second. Uncapped, WPF ticks animations roughly at your monitor's refresh rate, so this
   matters more on high-refresh displays.

That's the entire fix. No settings are changed inside LaunchBox itself, nothing is written to
disk beyond LaunchBox's own `Plugins` folder, and removing it is just deleting the DLL.

## What this does NOT affect

- **Game/emulator performance.** `RenderOptions.ProcessRenderMode` is scoped to the single
  process that sets it (`LaunchBox.exe` / `BigBox.exe`). When you launch a game, that's a
  separate process (RetroArch, Dolphin, PCSX2, MAME, a standalone emulator, whatever) with its
  own independent GPU context. This plugin never touches it.
- Anything system-wide, any other application, any driver setting.

## Trade-offs (read before installing)

- **CPU usage on LaunchBox/Big Box goes up.** In testing (15,600-game library, RTX 4070 Ti
  Super): GPU dropped from ~53% to **0%**, CPU on the LaunchBox process settled around
  **6-11%**. That's a good trade on any modern multi-core CPU, but it is a real trade, not a
  free lunch.
- **Anything relying on hardware video interop can silently stop rendering.** Some WPF features
  (e.g. `D3DImage`-based overlays, which some LaunchBox media features can use) require a real
  hardware D3D device and will render blank under `SoftwareOnly`. If you rely on in-app video
  previews/screenshot slideshows and notice them disappear, that's this trade-off, not a bug —
  see the "only want half of this" section below.
- Single-threaded software rasterization can feel very slightly less smooth during heavy
  animation than full hardware compositing, even with the frame-rate cap. It should not be
  noticeable during normal browsing.

### Only want half of this?

The two fixes are independent and either can be applied alone by commenting out the other line
in `GpuToCpuFixPlugin.cs`:

- Frame-rate cap only: keeps hardware rendering (and anything that depends on it) working,
  cuts GPU load noticeably (~53% → ~27% in testing) but not to zero, no CPU trade-off.
- Software-only rendering only: the biggest GPU win (~53% → 0%), highest CPU cost of the two,
  and the one that can break hardware video interop.

Combining both (the default in this repo) gave the best result in testing: 0% GPU with lower
CPU cost than software-only rendering alone, since the frame cap reduces how much work the
now-single-threaded compositor has to do.

## Installing

1. Build `src/LaunchBoxGpuToCpuFix.csproj` (see below), or grab `LaunchBoxGpuToCpuFix.dll` from
   [Releases](../../releases).
2. Copy the DLL into a new folder under your LaunchBox install, e.g.:
   `LaunchBox\Plugins\LaunchBoxGpuToCpuFix\LaunchBoxGpuToCpuFix.dll`
3. Restart LaunchBox / Big Box.

## Uninstalling

Delete the `LaunchBox\Plugins\LaunchBoxGpuToCpuFix` folder. That's the entire footprint.

## Building from source

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```
cd src
dotnet build -c Release
```

Before building, update the `HintPath` in `LaunchBoxGpuToCpuFix.csproj` to point at your own
`LaunchBox\Core\Unbroken.LaunchBox.Plugins.dll` — this repo doesn't redistribute Unbroken
Software's SDK assembly, your build has to reference the copy that ships with your own
LaunchBox install (it's marked `Private=false` so it isn't copied into your output — the
plugin binds to the copy already loaded by the host process at runtime).

## Tested against

- LaunchBox 13.27.0.0 (net9.0-windows / `Microsoft.WindowsDesktop.App` 9.0.16)
- ~15,600 games across 36 platforms
- NVIDIA RTX 4070 Ti Super, Windows 11

Should work on any LaunchBox/Big Box build on the net9.0 codebase, since it only touches public
WPF APIs and one small, documented plugin interface. If it breaks on a different version, open
an issue.

## Why share this

This isn't a bug in LaunchBox that needs reporting so much as an architectural default (WPF's
GPU-compositor-always-on behavior) that the app never exposes a toggle for. If your library is
big enough to notice, this fixes it without waiting on an upstream setting that may never come.
