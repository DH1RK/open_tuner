# libiio-sharp (C# binding of libiio)

The 15 `*.cs` files here are the C# binding of libiio 1.0 (`bindings/csharp` of https://github.com/analogdevicesinc/libiio),
copied unchanged: libiio `v1.0.0` + 30 commits (commit `48893bc9`, 2026-10-09). They are compiled into OpenTuner and used by
`ExtraFeatures\BATCSpectrum\IioSpectrumFeed.cs` (the ADI Pluto spectrum source, #215).

- License of the binding: MIT, see `COPYING_MIT.txt` and the header of every file (Copyright Analog Devices, Inc.).
- The native library it calls (`libiio1.dll`) is libiio itself, LGPL-2.1-or-later. It is not part of the repository; the build copies
  it into `libiio\` next to `opentuner.exe` (see `opentuner.csproj`, target `EnsureLibiioBundled`).
- Update: copy the files again from a newer libiio checkout; the API of 1.0 (`Stream`, `Block`, `ChannelsMask`) is what is used.
