# Wideband spectrum measurement tools

Small Node.js scripts (Node 22+, built-in `WebSocket`) that read the live FFT of the BATC QO-100 wideband spectrum
(`wss://eshail.batc.org.uk/wb/fft`, subprotocol `fft_m0dtslivetune`) - the same data the OpenTuner BATC spectrum uses -
to measure narrow signals. They were used to develop the narrow-signal symbol rate detection in
`ExtraFeatures/BATCSpectrum/signal.cs` (20 / 25 / 33 / 66 kS).

Facts found with them:

- The FFT has 922 bins over 9 MHz (9.76 kHz per bin), values are **1/4096 dB** (65535 = 16.0 dB = clipping).
- Converted to linear power (noise subtracted) the half-power width (FWHM) does not depend on the level and follows
  `FWHM = sqrt(SR^2 + 10.7^2)` kHz: 20 kS = 23.1, 25 kS = 27.1, 33 kS = 34.7, 66 kS = 67.8 kHz (measured).
- A signal with a clipped top (16.0 dB) is measured too wide (25 kS at 16 dB looks like 33 kS).

| Script | Purpose |
|---|---|
| `wbwidth.js <label> [frames]` | averaged profile + linear widths around the peak near 10498.50 MHz, saves `profile_<label>.json` |
| `wbsurvey.js [frames] [maxWidthKHz]` | lists all narrow signals in the band |
| `wbprofile.js`, `wbbins.js` | save / print the raw bins around a frequency |
| `wbanalyze.js`, `wbclip.js`, `wbmodel.js` | offline experiments on the saved profiles (width estimators, clipped-peak reconstruction, model fit) |
| `wbapp.js [frames]` | 1:1 mirror of `signal.cs` `detect_signals` + narrow measure on live frames |
| `wbframes.js [frames]` | frame-to-frame scatter of the FWHM estimate |

`profile_*.json` are the recorded profiles (60 frames averaged) of 20 / 25 / 33 / 66 kS signals sent from a Pluto on
10498.5 MHz at different levels (`33_bake` = 8.5 dB, `33_mid` = 15 dB, `*_strong` / `25_app` = clipped).
