# Disc Image Studio — English Edition

This is an English-only build of [Disc Image Studio](https://github.com/JiaFeiMiao-K-Cat/DiscImageStudio)
(GPL-3.0-or-later), based on upstream `main` as of 2026-11-04.

## What's different from upstream

- **The interface is always English.** The saved and system language settings are ignored, and the
  language picker in Settings is hidden.
- **No Chinese fallback.** Upstream shows the Chinese text whenever a string has no translation. This
  edition never does.
- **About 350 hard-coded Chinese strings translated.** Upstream's English mode still showed Chinese
  for these, mostly in:
  - burn progress, drive and IMAPI errors, and the Blu-ray write diagnostics
  - calibration fitting warnings and photo rectification messages
  - project and preset file validation errors
  - the CD/DVD/Blu-ray preset names and descriptions
- `README.md` is now the English README.

Everything else is unchanged: disc generation, calibration, burning and the command-line interface.

## Added: HoloStudio3D

`HoloStudio3D.exe` is built next to the app. Its Stage 1 measures your discs with the bundled
disc-probe and installs a preset, and it makes Hologram mode discs (2-7 views), hidden-3D stereograms and
experimental diffraction (tilt-image) DVDs. See `HOLOSTUDIO3D.md`.

## Build (Windows)

1. Install the **.NET 10 SDK**: https://dotnet.microsoft.com/download/dotnet/10.0
2. Double-click **`Build-English.bat`**.
3. Your apps are `HoloStudio3D-App\HoloStudio3D.exe` and `HoloStudio3D-App\DiscImageStudio.exe` (the folder opens by itself when the build finishes). Each is a
   single self-contained file, so the computer running it doesn't need .NET installed.

## Notes

- The regression tests under `tests/` were not changed. Some of them check for the original Chinese
  text, so expect those checks to fail on this edition. The app build doesn't depend on them.
- Live burning is experimental upstream. Use blank discs you don't mind wasting.

## Credits and license

- **English edition and HoloStudio3D:** Drew Medina, [drewmedina.com](https://drewmedina.com)
- **Disc Image Studio:** JiaFeiMiao-K-Cat and the Disc Image Studio contributors,
  <https://github.com/JiaFeiMiao-K-Cat/DiscImageStudio>
- **disc-probe** (HoloStudio3D's Stage 1, bundled unmodified in `tools/disc-probe`): Ashley-QwQ,
  <https://github.com/Ashley-QwQ/disc-probe>
- Other components and their licenses are listed in `THIRD-PARTY-NOTICES.txt`.

All three projects are licensed under the GNU GPL version 3 or later (see `LICENSE`). If you share
this edition, keep `LICENSE` and `THIRD-PARTY-NOTICES.txt` with it and make this source folder
available alongside the program. It is a modified version, not an official Disc Image Studio
release, and comes with no warranty.
