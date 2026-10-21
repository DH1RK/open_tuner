# Prerequisites

## To run a release (zip from the releases page)

- **.NET 10 Desktop Runtime** (x64) - https://dotnet.microsoft.com/download/dotnet/10.0
  Required to run `opentuner.exe` at all (the build is framework-dependent, not
  self-contained).

That is all for the players FFMPEG (FlyleafLib, the default) and VLC: the zip already contains
the FFmpeg shared libraries (folder `ffmpeg\` next to `opentuner.exe`) and the VLC libraries
(folder `libvlc\`). Nothing else has to be downloaded or configured.

## Optional: MPV player

If you select **MPV** as a media player in Settings, you additionally need `libmpv-2.dll`
(a 64-bit Windows build), e.g. from
https://sourceforge.net/projects/mpv-player-windows/files/libmpv/
The project publishes new builds regularly and removes old ones, so take the newest file
`mpv-dev-x86_64-<date>-git-<hash>.7z` (the `-v3` variant needs a CPU with AVX2).
**Tested against: `mpv-dev-x86_64-20261018-git-01e2533f31` (shinchiro/mpv-player-windows, 2026-10-18).**

1. Download and extract a recent 64-bit `libmpv` build somewhere permanent (outside of
   OpenTuner's own folder, so that an update does not remove it)
2. In OpenTuner: **Settings > Playback Paths > libmpv Path** - point it at the folder
   containing `libmpv-2.dll`
3. Restart OpenTuner (this setting is only read at startup)

If left empty, OpenTuner falls back to the default DLL search order, i.e. `libmpv-2.dll` has
to sit directly next to `opentuner.exe`.

If a library is missing at startup, OpenTuner shows a one-time setup dialog with a download
link and a folder picker; "Skip for now" falls back to the bundled `ffmpeg\` folder or the
default DLL search order. The dialog writes into **Settings > Playback Paths**, which you can
also edit by hand at any time.

## Optional: another FFmpeg build

The `ffmpeg\` folder in the zip is used when **Settings > Playback Paths > ffmpeg Path** is
empty or does not point at a valid folder. To use your own build instead, download a **shared**
Windows build (not `static`), whose major version matches the `Flyleaf.FFmpeg.ABI`
package that FlyleafLib depends on (**tested against FFmpeg 9.0.2**). The tested build
is `ffmpeg-9.0.2-full_build-shared.7z` (or `.zip`) at
https://github.com/GyanD/codexffmpeg/releases/tag/9.0.2 (more builds:
https://www.gyan.dev/ffmpeg/builds/). What changed in FFmpeg 9.0.2:
[Changelog](https://github.com/FFmpeg/FFmpeg/blob/n9.0.2/Changelog), and since 9.0.1:
[compare](https://github.com/FFmpeg/FFmpeg/compare/n9.0.1...n9.0.2). Point **ffmpeg Path** at its `bin\` folder (the one
containing `avcodec-*.dll`, `avformat-*.dll`, etc.). Restart OpenTuner afterwards.

## To build from source

- **.NET 10 SDK** (x64) - https://dotnet.microsoft.com/download/dotnet/10.0
- A FFmpeg **shared** build as described above. The build copies its `av*.dll` and `sw*.dll`
  into `ffmpeg\` of the output folder. The default source folder is
  `D:\Video\ffmpeg-9.0.2-full_build-shared\bin\`; use
  `-p:FfmpegSharedBuildPath=<your bin folder>` to set another one.

## Not required separately

- **VLC playback** (LibVLCSharp) ships its native libraries via the `VideoLAN.LibVLC.Windows`
  NuGet package - nothing to download manually.
