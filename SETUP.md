# Prerequisites

OpenTuner targets .NET 10 and pulls in most of its dependencies via NuGet automatically
(LibVLC, NAudio, LibUsbDotNet, etc.). A handful of native libraries can't be NuGet-managed
because their exact version has to match what the app was built against, so they need to be
downloaded separately and pointed at via Settings.

## To run a built copy

- **.NET 10 Desktop Runtime** (x64) - https://dotnet.microsoft.com/download/dotnet/10.0
  Required to run `opentuner.exe` at all (the build is framework-dependent, not
  self-contained).

## To build from source

- **.NET 10 SDK** (x64) - https://dotnet.microsoft.com/download/dotnet/10.0

## Suggested layout

Pick one permanent folder outside of OpenTuner's own `bin\` (that folder gets wiped on every
Clean/Rebuild) to hold both native dependencies. **Settings > Playback Paths** defaults to the
exact folders this migration was built/tested against:

```
D:\Video\ffmpeg-9.0.1-full_build-shared\bin\   <- ffmpeg Path (default)
D:\Video\mpv-dev-x86_64-20260903\              <- libmpv Path (default)
```

If those folders don't exist on your machine, OpenTuner shows a warning at startup and falls
back automatically (bundled `ffmpeg\` folder / default DLL search order - see below) - update
both fields under **Settings > Playback Paths** to wherever you actually put things.

## Optional: native VVC (H.266) / general video playback via FlyleafLib

The default media player uses FlyleafLib, which needs a shared-library FFmpeg build whose
major version matches the `Flyleaf.FFmpeg.Bindings` package referenced in `opentuner.csproj`.
**Tested against: FFmpeg 9.0.1 (shared build).**

1. Download a matching **shared** Windows build, e.g. from
   https://www.gyan.dev/ffmpeg/builds/ (pick a `shared` build, not `static`/`full_build`
   without "shared" in the name - only the shared build ships the `avcodec-*.dll` etc. files
   FlyleafLib loads at runtime; a rolling/git-dev build works too as long as its major version
   still matches `Flyleaf.FFmpeg.Bindings` - check `opentuner.csproj` for the exact version this
   was built against if in doubt)
2. Extract it somewhere permanent (see Suggested layout above)
3. In OpenTuner: **Settings > Playback Paths > ffmpeg Path** - point it at that build's `bin\`
   folder (the one containing `avcodec-*.dll`, `avformat-*.dll`, etc.)
4. Restart OpenTuner (this setting is only read at startup)

If left empty, OpenTuner falls back to looking for a `ffmpeg\` folder next to `opentuner.exe`.

## Optional: MPV player

If you select **MPV** as a media player in Settings, you additionally need `libmpv-2.dll`
(a 64-bit Windows build), e.g. from
https://sourceforge.net/projects/mpv-player-windows/files/libmpv/
**Tested against: a shinchiro/mpv-player-windows build from 2026-09-03.**

1. Download and extract a recent 64-bit `libmpv` build somewhere permanent (see Suggested
   layout above)
2. In OpenTuner: **Settings > Playback Paths > libmpv Path** - point it at the folder
   containing `libmpv-2.dll`
3. Restart OpenTuner (this setting is only read at startup)

If left empty, OpenTuner falls back to the default DLL search order, i.e. `libmpv-2.dll` has
to sit directly next to `opentuner.exe`.

## Not required separately

- **VLC playback** (LibVLCSharp) ships its native libraries via the `VideoLAN.LibVLC.Windows`
  NuGet package - nothing to download manually.
