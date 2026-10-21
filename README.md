# Open Tuner
Open Windows Software for use with DATV tuner variants based on the Minitiouner range.

## Main reasons for making these changes:
- An all-in-one solution for handling four TS streams (QSO full-duplex or supervision)
- More advanced software features compared to the abandoned and outdated MiniTioune software
- Support for VVC (Versatile Video Coding, H.266)

## Intention
My goal is to enhance the OpenTuner software developed by Tom, ZR6TG, and to give the community a more capable and useful application for everyone.
If you use and enjoy my software and would like to support the development, then you can donate via PayPal:

[![Donate via PayPal.me](https://img.shields.io/badge/PayPal.me-Donate-0070BA?logo=paypal&logoColor=white)](https://paypal.me/dh1rk)

## Bugs etc.
I track bugs, features, and other work items using GitHub Issues. Please use the issue templates and select your release and hardware there. I will assign the milestone (release) and project during triage.

- 🐞 Found a bug or have a wish? [Open an issue](https://github.com/DH1RK/open_tuner/issues/new/choose) (please look at the [open issues](https://github.com/DH1RK/open_tuner/issues) first)
- 📋 What changed in which release? The [project board](https://github.com/users/DH1RK/projects/1) gives the overview of all changes per release.

## Downloads and setup
[Compiled development (D) and official (R) versions](https://github.com/DH1RK/open_tuner/releases)

[Prerequisites / setup](SETUP.md) - what to download and where to point OpenTuner at it

[More info](https://www.zr6tg.co.za/open-tuner/)

## Devices mainly tested with this fork:
- MiniTiouner Pro (with two USB ports)
- MiniTiouner V2 (Elad E-Tiouner, 1 USB port)
### Remark
Unfortunately, I don't have any other hardware, so I can't test it on a different device either.

## General information
I will continue to develop and improve the software in my spare time and make these changes available here free of charge.

Releases are named `D<main>.<number>` for a development release (e.g. `D00.09`) and `R<main>.<number>` for an official release (e.g. `R01.00`), both parts with two digits so that the lists sort correctly. The number goes up with every change package. The version is shown in the window title, e.g. `OpenTuner (DH1RK - D00.09)`.

For me, a development release is a version that can be used by a broader group of users.

Key characteristics of a development release:

✅ The main features have been implemented.
✅ The software is generally usable.
✅ Known and unknown issues are still to be expected.
✅ Users are encouraged to provide feedback and submit bug reports.
✅ Feature changes are kept to a minimum, with the focus shifting towards stabilization and bug fixing.

## License and disclaimer
OpenTuner is licensed under the GNU General Public License v3.0 (see [LICENSE](LICENSE)). The licenses of the components it uses (FFmpeg, libmpv, VLC, NuGet packages) are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md); both files are in the release zip. It is provided "as is", without warranty of any kind. You use it, and the hardware it controls (LNB supply, EXTERN outputs, transmit functions), at your own risk.
