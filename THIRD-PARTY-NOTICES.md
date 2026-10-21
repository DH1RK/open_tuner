# Third-party notices

OpenTuner is licensed under the **GNU General Public License v3.0** (see `LICENSE`). The source code of every release is
at https://github.com/DH1RK/open_tuner (the tag of the release, e.g. `D00.09`, is the code the program was built from).

The program uses the components below. Versions are those of `opentuner.csproj` and of the files in the release zip;
the licenses of the NuGet packages are the ones named in the package (`.nuspec`). This list is a notice, not legal advice.

## Components in the release zip that are not NuGet packages

| Component | Version | License | Source / more information |
|---|---|---|---|
| FFmpeg shared libraries (folder `ffmpeg\`) | 9.0.2, "full_build" of gyan.dev | **GPL-3.0-or-later** as a whole (the build is configured with `--enable-gpl --enable-version3`; it contains many libraries of their own, for example x264, x265, libaom, dav1d) | FFmpeg source of the version: https://github.com/FFmpeg/FFmpeg/tree/n9.0.2 - the build and its configuration: https://www.gyan.dev/ffmpeg/builds/ and https://github.com/GyanD/codexffmpeg/releases/tag/9.0.2 |
| libmpv (`libmpv-2.dll`) | 0.41.0 (`v0.41.0-1205-g01e2533f3`) | **GPL-2.0-or-later** (the text in the file: "mpv is distributed under the terms of the GNU General Public License Version 2 or later") | https://github.com/mpv-player/mpv - the build: https://sourceforge.net/projects/mpv-player-windows/files/libmpv/ |
| VLC libraries (folder `libvlc\`) | 3.0.24 (NuGet `VideoLAN.LibVLC.Windows`) | LGPL-2.1-or-later (as named in the package; the single plugins were not checked) | https://code.videolan.org/videolan/vlc - the package: https://code.videolan.org/videolan/libvlc-nuget |
| `FTD2XX_NET.dll` | 1.1.0.0 | Copyright FTDI 2009-2013, .NET wrapper for the D2XX driver. **The license terms were not checked yet.** The D2XX driver itself is not part of the zip. | https://ftdichip.com |

GPL-3.0-or-later (FFmpeg) and GPL-2.0-or-later (mpv) can be combined with the GPL-3.0 of OpenTuner.
The FFmpeg and libmpv builds can be replaced by other builds (Settings > Playback Paths, see `SETUP.md`).

## NuGet packages

| Package | Version | License | Project |
|---|---|---|---|
| CoreAudio | 1.40.0 | MIT | https://github.com/morphx666/CoreAudio |
| FlyleafLib | 3.11.13 | LGPL-3.0-or-later | https://github.com/SuRGeoNix/Flyleaf |
| LibUsbDotNet | 3.0.224 | LGPL-3.0-or-later | https://github.com/LibUsbDotNet/LibUsbDotNet |
| LibVLCSharp, LibVLCSharp.WinForms | 3.10.2 | LGPL-2.1-or-later | https://code.videolan.org/videolan/LibVLCSharp |
| MQTTnet | 4.3.3.952 | MIT | https://github.com/dotnet/MQTTnet |
| NAudio, NAudio.Asio, NAudio.Core, NAudio.Midi, NAudio.Wasapi, NAudio.WinForms, NAudio.WinMM | 2.2.1 | MIT | https://github.com/naudio/NAudio |
| Newtonsoft.Json | 13.0.4 | MIT | https://www.newtonsoft.com/json |
| Serilog | 3.1.2-dev-02097 | Apache-2.0 | https://serilog.net/ |
| Serilog.Sinks.Console | 5.0.1 | Apache-2.0 | https://github.com/serilog/serilog-sinks-console |
| Serilog.Sinks.File | 5.0.0 | Apache-2.0 | https://serilog.net/ |
| SocketIOClient | 3.0.8 | MIT | https://github.com/doghappy/socket.io-client-csharp |
| Vortice.D3DCompiler, Vortice.Direct2D1, Vortice.Direct3D11, Vortice.DirectComposition, Vortice.DirectX, Vortice.DXGI, Vortice.MediaFoundation, Vortice.XAudio2 | 3.8.3 | MIT | https://github.com/amerkoleci/Vortice.Windows |
| WebSocketSharp | 1.0.3-rc11 | MIT (the `LICENSE.txt` of the project) | https://github.com/sta/websocket-sharp |

The full texts: GNU GPL and LGPL https://www.gnu.org/licenses/ - Apache-2.0 https://www.apache.org/licenses/LICENSE-2.0 -
MIT below. The Apache-2.0 packages (Serilog) bring no NOTICE file that has to be repeated here.

## Copyright holders of the MIT-licensed packages

- CoreAudio: morphx666
- MQTTnet: .NET Foundation and Contributors
- NAudio: Copyright 2020 Mark Heath
- Newtonsoft.Json: Copyright (c) James Newton-King 2008
- SocketIOClient: HeroWong
- Vortice.*: Copyright (c) Amer Koleci and Contributors
- WebSocketSharp: Copyright (c) 2010-2016 sta.blockhead

## The MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
