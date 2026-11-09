# disc-probe (vendored)

This folder is an unmodified copy of **disc-probe** by Ashley-QwQ
(https://github.com/Ashley-QwQ/disc-probe), GPL-3.0 (see `LICENSE`), taken at commit
`f5d71da6a11417c2c245ebf161cb581d4c60b213` (2026-11-08). The `evidence/` folder and the photos
under `docs/` are left out to keep the package small; everything the tool runs is here.

HoloStudio3D uses it as **Stage 1: Measure your discs**. It runs it through
`tools/Disc3DLab/discprobe-bridge/labrun.py` with a private Python that `Build-English.ps1` sets
up next to the program. disc-probe's drive code only sends read commands (see its README,
"Safety"); burning is always left to Windows or ImgBurn.

To update it: replace this folder's contents with a newer disc-probe checkout (keep this file).
