# Third-party notices

OpenTuner is licensed under the **GNU General Public License v3.0** (see `LICENSE`). The source code of every release is
at https://github.com/DH1RK/open_tuner (the tag of the release, e.g. `D00.10`, is the code the program was built from).

The program uses the components below. Versions are those of `opentuner.csproj` and of the files in the release zip;
the licenses of the NuGet packages are the ones named in the package (`.nuspec`). This list is a notice, not legal advice.

## Components in the release zip that are not NuGet packages

| Component | Version | License | Source / more information |
|---|---|---|---|
| FFmpeg shared libraries (folder `ffmpeg\`) | 9.0.2, "full_build" of gyan.dev | **GPL-3.0-or-later** as a whole (the build is configured with `--enable-gpl --enable-version3`; it contains many libraries of their own, for example x264, x265, libaom, dav1d) | FFmpeg source of the version: https://github.com/FFmpeg/FFmpeg/tree/n9.0.2 - the build and its configuration: https://www.gyan.dev/ffmpeg/builds/ and https://github.com/GyanD/codexffmpeg/releases/tag/9.0.2 |
| libmpv (`libmpv-2.dll`) | 0.41.0 (`v0.41.0-1205-g01e2533f3`) | **GPL-2.0-or-later** (the text in the file: "mpv is distributed under the terms of the GNU General Public License Version 2 or later") | https://github.com/mpv-player/mpv - the build: https://sourceforge.net/projects/mpv-player-windows/files/libmpv/ |
| VLC libraries (folder `libvlc\`) | 3.0.24 (NuGet `VideoLAN.LibVLC.Windows`) | LGPL-2.1-or-later (as named in the package; the single plugins were not checked) | https://code.videolan.org/videolan/vlc - the package: https://code.videolan.org/videolan/libvlc-nuget |
| `FTD2XX_NET.dll` | 1.1.0.0 | Copyright FTDI 2009-2013, .NET wrapper for the D2XX driver. Used unmodified, only as the access layer to FTDI's D2XX driver (it passes calls on to the driver). FTDI's "Driver Licence Terms" are included in full: `licenses\FTDI-driver-licence-terms.txt`. The D2XX driver itself is not part of the zip. | https://ftdichip.com/drivers/d2xx-drivers/ - https://ftdichip.com/driver-licence-terms-details/ |
| libiio 1.0 (`libiio\libiio1.dll`) | 1.0.0 (release v1.0.0, unsigned CI build "Windows-VS-2022-x64") | LGPL-2.1-or-later (the library; the iio-utils of the project, which are not in the zip, are GPL-2.0-or-later) | https://github.com/analogdevicesinc/libiio (release: https://github.com/analogdevicesinc/libiio/releases/tag/v1.0.0). Used unmodified, for the spectrum source "ADI Pluto" |
| libusb (`libusb-1.0.dll`, `libiio\libusb-1.0.dll`) | 1.0.27 | LGPL-2.1-or-later (as named in the file) | https://libusb.info, https://github.com/libusb/libusb |
| libserialport (`libiio\libserialport.dll`) | as built for the libiio release (libiio needs 0.1.1 or later) | LGPL-3.0-or-later (the project: "released under the LGPL3+ license") | https://sigrok.org/wiki/Libserialport |
| libxml2 (`libiio\libxml2.dll`) | 2.9.14 | MIT, Copyright (C) Daniel Veillard (text below) | https://gitlab.gnome.org/GNOME/libxml2 |
| Zstandard (`libiio\libzstd.dll`) | 1.5.6 | BSD-3-Clause (the README of zstd 1.5.6: "dual-licensed under BSD OR GPLv2", the BSD in its file `LICENSE`; used here under the BSD, text in `licenses\BSD-3-Clause-zstd.txt`), Copyright (c) Meta Platforms, Inc. and affiliates | https://github.com/facebook/zstd |
| Microsoft Visual C++ runtime (`libiio\msvcp140.dll`, `libiio\vcruntime140.dll`) | VS 2022 | Redistributable files of Microsoft Visual C++ (license terms of the Visual Studio redistributables), copied unchanged from the libiio release | https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files |
| libiio-sharp (compiled into `opentuner.exe`; source in `ThirdParty\libiio-sharp\`) | libiio v1.0.0 + 30 commits (commit 48893bc9), `bindings/csharp` | MIT, Copyright (C) Analog Devices, Inc. (text in `ThirdParty\libiio-sharp\COPYING_MIT.txt` and below) | https://github.com/analogdevicesinc/libiio/tree/main/bindings/csharp |

GPL-3.0-or-later (FFmpeg) and GPL-2.0-or-later (mpv) can be combined with the GPL-3.0 of OpenTuner.
The FFmpeg and libmpv builds can be replaced by other builds (Settings > Playback Paths, see `SETUP.md`).
The libraries in the folder `libiio\` are separate DLLs that OpenTuner loads at run time (libiio-sharp calls only `libiio1.dll`); they can
be replaced by other builds of the same version (the LGPL freedom to change and relink them), e.g. the Windows build of the libiio
release linked in the table. Without the folder the ADI Pluto spectrum source reports "libiio not found" and everything else works.

## Source code of the GPL components (written offer)

The zip contains the FFmpeg build of gyan.dev (GPL-3.0-or-later) and
libmpv (GPL-2.0-or-later) as binaries; the LGPL libraries libiio, libusb and libserialport are binaries of the
libiio release v1.0.0 (their source code: https://github.com/analogdevicesinc/libiio, https://github.com/libusb/libusb,
https://github.com/sigrokproject/libserialport). The source code of the GPL components is public: FFmpeg 9.0.2 at https://github.com/FFmpeg/FFmpeg/tree/n9.0.2,
the build scripts and the list of the libraries inside the "full_build" at https://github.com/GyanD/codexffmpeg/releases/tag/9.0.2
and https://www.gyan.dev/ffmpeg/builds/, mpv at https://github.com/mpv-player/mpv (tag `v0.41.0`). If you cannot get the source
code of a component from there, open an issue at https://github.com/DH1RK/open_tuner/issues and we will send it to you or tell
you where it is; this offer is valid for three years after the release of the zip (and as long as we distribute the binaries).

## FTDI hardware

FTDI's license allows its D2XX driver only with genuine FTDI components (chips manufactured for and sold by
FTDI); using it with a counterfeit chip may irretrievably damage that chip. Liability of FTDI is limited (clause 6 of its
terms). OpenTuner gives no warranty either (see `LICENSE`).

## DeepL and Azure Translator (optional translation of the Wideband Chat)

If the user switches "Translate" on in the Wideband Chat, the text of the chat messages is sent to the translation service chosen in the chat settings, with the user's own API key, which is read from a file of the user and is neither part of the program nor stored in its settings: either the DeepL API (https://www.deepl.com/pro-api, key file `.deepl_key` in the user directory by default, terms: https://www.deepl.com/pro-license) or Azure Translator of Microsoft (https://azure.microsoft.com/products/ai-services/ai-translator, key file `.azure_translator_key` by default, terms: https://www.microsoft.com/licensing/terms). No software of DeepL or Microsoft for this is part of the zip. Without a key file nothing is sent.

## NuGet packages

| Package | Version | License | Project |
|---|---|---|---|
| CoreAudio | 1.40.0 | MIT | https://github.com/morphx666/CoreAudio |
| Flyleaf.FFmpeg.ABI | 9.0.0 | MIT | https://github.com/SuRGeoNix/Flyleaf.FFmpeg.ABI |
| FlyleafLib | 3.11.17 | LGPL-3.0-or-later | https://github.com/SuRGeoNix/Flyleaf |
| LibUsbDotNet | 3.0.224 | LGPL-3.0-or-later | https://github.com/LibUsbDotNet/LibUsbDotNet |
| LibVLCSharp, LibVLCSharp.WinForms | 3.10.2 | LGPL-2.1-or-later | https://code.videolan.org/videolan/LibVLCSharp |
| Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.Extensions.Logging, Microsoft.Extensions.Logging.Abstractions, Microsoft.Extensions.Options, Microsoft.Extensions.Primitives | 10.0.3 | MIT | https://dot.net/ |
| MQTTnet | 5.2.0.1603 | MIT | https://github.com/dotnet/MQTTnet |
| Newtonsoft.Json | 13.0.4 | MIT | https://www.newtonsoft.com/json |
| Serilog | 4.4.0 | Apache-2.0 | https://serilog.net/ |
| Serilog.Sinks.Console | 6.1.1 | Apache-2.0 | https://github.com/serilog/serilog-sinks-console |
| Serilog.Sinks.File | 7.0.0 | Apache-2.0 | https://serilog.net/ |
| SharpGen.Runtime, SharpGen.Runtime.COM | 2.4.2-beta | MIT | https://github.com/SharpGenTools/SharpGenTools |
| SocketIOClient, SocketIOClient.Common, SocketIOClient.Serializer | 4.0.5, 4.0.0, 4.0.0.1 | MIT | https://github.com/doghappy/socket.io-client-csharp |
| Vortice.Mathematics | 2.1.1 | MIT | https://github.com/amerkoleci/Vortice.Mathematics |
| Vortice.D3DCompiler, Vortice.Direct2D1, Vortice.Direct3D11, Vortice.DirectComposition, Vortice.DirectX, Vortice.DXGI, Vortice.MediaFoundation, Vortice.XAudio2 | 3.8.3 | MIT | https://github.com/amerkoleci/Vortice.Windows |

The full texts of the licenses are in the folder `licenses\` of the release zip (also in the repository): `lgpl-2.1.txt`,
`lgpl-3.0.txt`, `gpl-2.0.txt`, `Apache-2.0.txt`, `BSD-3-Clause-zstd.txt`, and the FTDI terms `FTDI-driver-licence-terms.txt`; the GPL-3.0 is
`LICENSE` in the root, the MIT license is below. They are the texts of https://www.gnu.org/licenses/ and
https://www.apache.org/licenses/LICENSE-2.0 (the FTDI terms: https://ftdichip.com/driver-licence-terms-details/). The
Apache-2.0 packages (Serilog) bring no NOTICE file that has to be repeated here.

## Copyright holders of the MIT-licensed packages

- libiio-sharp (libiio C# binding): Copyright (C) 2015 Analog Devices, Inc.
- libxml2: Copyright (C) 1998-2012 Daniel Veillard. All Rights Reserved.
- CoreAudio: morphx666
- Flyleaf.FFmpeg.ABI: Copyright (c) 2026 SuRGeoNix
- Microsoft.Extensions.*: Copyright (c) Microsoft Corporation. All rights reserved.
- MQTTnet: .NET Foundation and Contributors
- Newtonsoft.Json: Copyright (c) James Newton-King 2008
- SharpGen.Runtime (incl. COM): (c) 2010-2017 Alexandre Mutel, 2017-2023 Jeremy Koritzinsky, 2023-2024 Amer Koleci
- SocketIOClient (incl. Common, Serializer): HeroWong
- Vortice.* (incl. Vortice.Mathematics): Copyright (c) Amer Koleci and Contributors

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
