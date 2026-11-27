# Prerequisites

## To run a release (zip from the releases page)

- **.NET 10 Desktop Runtime** (x64) - https://dotnet.microsoft.com/download/dotnet/10.0
  Required to run `opentuner.exe` at all (the build is framework-dependent, not
  self-contained).

That is all for the players FFMPEG (FlyleafLib, the default), VLC and MPV: the zip already contains
the FFmpeg shared libraries (folder `ffmpeg\` next to `opentuner.exe`), the VLC libraries
(folder `libvlc\`) and `libmpv-2.dll` (next to `opentuner.exe`). Nothing else has to be downloaded.

If OpenTuner shows the playback setup dialog at the first start (the default paths point to folders
that only exist on the developer's PC), choose "Skip for now": the files of the zip are used then.

## Optional: another libmpv build

The MPV player uses the `libmpv-2.dll` next to `opentuner.exe` (it is in the zip) as long as
**Settings > Playback Paths > libmpv Path** is empty or does not point at an existing folder.
To use a different (e.g. newer) 64-bit build, take one from
https://sourceforge.net/projects/mpv-player-windows/files/libmpv/
The project publishes new builds regularly and removes old ones, so take the newest file
`mpv-dev-x86_64-<date>-git-<hash>.7z` (the `-v3` variant needs a CPU with AVX2).
**Tested against: `mpv-dev-x86_64-20261018-git-01e2533f31` (shinchiro/mpv-player-windows, 2026-10-18).**

1. Download and extract a recent 64-bit `libmpv` build somewhere permanent (outside of
   OpenTuner's own folder, so that an update does not remove it)
2. In OpenTuner: **Settings > Playback Paths > libmpv Path** - point it at the folder
   containing `libmpv-2.dll`
3. Restart OpenTuner (this setting is only read at startup)

If left empty, OpenTuner falls back to the default DLL search order, i.e. to the `libmpv-2.dll`
next to `opentuner.exe`.

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

## Optional: translation of the Wideband Chat

**Translate** in the Wideband Chat uses **Azure Translator** (Microsoft) with your own key. The text of the chat
messages is sent to Microsoft. The free tier **F0** covers 2 million characters per month
([pricing](https://azure.microsoft.com/en-us/pricing/details/cognitive-services/translator-text-api/)).
Releases up to D01.02 used DeepL; a DeepL key does not work any more.
The names in the Azure portal can change; Microsoft describes the steps in
[Create an Azure Translator resource](https://learn.microsoft.com/en-us/azure/ai-services/translator/how-to/create-translator-resource).

### 1. Azure account

Create one at https://azure.microsoft.com/en-us/pricing/purchase-options/azure-account (Microsoft account, phone
number and credit or debit card). A new free account has to be moved to **pay-as-you-go** to continue beyond the
first 30 days; you then pay only for what you use beyond the free monthly amounts. Keep the Translator resource on
the tier **F0**.

### 2. Translator resource

In https://portal.azure.com search for **Translator** (by Microsoft), choose **Create** and fill in:

| Field | Value |
|---|---|
| Subscription | your subscription |
| Resource group | a new one, e.g. `rg-translator` |
| Region | **Global** |
| Name | any free name |
| Pricing tier | **Free F0** (one per subscription) |

Then **Review + create**, **Create** and **Go to resource**. The key is under
**Resource Management > Keys and Endpoint**.

If the portal does not offer the form, use the **Cloud Shell** (symbol `>_` at the top of the portal, **Bash**,
"No storage account required"). Replace `<name>` with a free name and run the commands one after the other:

```bash
az provider register --namespace Microsoft.CognitiveServices --wait
az group create --name rg-translator --location westeurope
az cognitiveservices account create --name <name> --resource-group rg-translator --kind TextTranslation --sku F0 --location global --yes
az cognitiveservices account keys list --name <name> --resource-group rg-translator --query key1 --output tsv
```

The last one shows the key.

### 3. Key file

Press **Win+R**, run `notepad %USERPROFILE%\.azure_translator_key`, let Notepad create the file and enter two lines:

```
<your key 1>
global
```

Line 2 is the region of the resource (`global`, or for a regional resource its short name, e.g. `germanywestcentral`).
The key is a password: keep the file to yourself.

### 4. In OpenTuner

1. Open the settings of the Wideband Chat and choose the language in **Translate to**. The window shows
   "Key file found" (another file can be chosen with **Browse**).
2. In the chat window set **Translate**.

The bar below **Translate** shows how much of the free tier OpenTuner has used this month on this PC. When the
translation does not work, **Translate** turns red and its tooltip gives the reason.

## To build from source

- **.NET 10 SDK** (x64) - https://dotnet.microsoft.com/download/dotnet/10.0
- A FFmpeg **shared** build as described above. The build copies its `av*.dll` and `sw*.dll`
  into `ffmpeg\` of the output folder. The default source folder is
  `D:\Video\ffmpeg-9.0.2-full_build-shared\bin\`; use
  `-p:FfmpegSharedBuildPath=<your bin folder>` to set another one.
- **Optional, for the spectrum source "ADI Pluto (libiio)" and the PicoTuner:** the native libiio 1.0 files. The release zip
  contains them in `libiio\`, and their `libusb-1.0.dll` also next to `opentuner.exe`. A build from source copies them only when it is told where they are; without
  them the source says "libiio not found" when it is chosen, a PicoTuner works only if the PC has a `libusb-1.0.dll` of its own, and everything else works.
  1. Download `Windows.zip` of the libiio release
     https://github.com/analogdevicesinc/libiio/releases/tag/v1.0.0 and unpack it.
  2. Point the build at the folder `Windows-VS-2022-x64\Release\` of it (x64, VS 2022 build): either
     `-p:LibiioNativePath=<that folder>\` on the command line, or once per machine in the file
     `LibiioNative.local.props` next to `opentuner.csproj` (it is not versioned, see `.gitignore`):
     ```xml
     <Project>
       <PropertyGroup>
         <LibiioNativePath>C:\path\to\Windows-VS-2022-x64\Release\</LibiioNativePath>
       </PropertyGroup>
     </Project>
     ```
  3. The build copies the DLLs to `libiio\` of the output folder (not `libiio.dll`, the compatibility layer
     for old v0.x programs). `scripts\Build-ReleaseZip.ps1` stops when they are missing there, so a release
     zip always contains them. Their licenses are listed in `THIRD-PARTY-NOTICES.md`.

## Not required separately

- **VLC playback** (LibVLCSharp) ships its native libraries via the `VideoLAN.LibVLC.Windows`
  NuGet package - nothing to download manually.
