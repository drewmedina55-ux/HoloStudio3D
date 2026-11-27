# HoloStudio3D release notes

What changed in each version of HoloStudio3D. Every release is the full app: download the newest
one and replace your old `HoloStudio3D-App` folder. Your settings and presets are kept.

## v1.10 (2026-11-27)

- **Fixed:** installing a second measured disc type in Stage 1 failed with "preset 'discprobe-dvd-r'
  is already in ... with different values". Each preset name now gets its own preset. Untick
  "Replace my earlier preset with the same name" to keep an older one with the same name too.
- **Fixed:** Stage 1 could reuse a correction file left over from an earlier measurement, which
  doesn't fit a new one (disc-probe only warned). It's now made again from the measurement you're
  installing.
- **New: preset files** (Stage 1, step 5). Save the current preset to a file, one per disc type,
  and open preset files from anywhere. Files in `Documents\HoloStudio3D\Presets` show up in every
  disc list as "File: ...". They use Disc Image Studio's own preset format, so older versions and
  Disc Image Studio can read them too, and your existing presets aren't touched.
- **New: "Open…" next to every disc list.** Use a preset file straight away, without Stage 1.
- The build file is now called **`INSTALL-BUILD.bat`** (it was `Build-English.bat`).
- Stage 1 is now at the top of the list, and its two-line entry no longer overlaps when Windows
  scales text up.

## v1.09 (2026-11-26)

- **New, experimental: "View patterns: Ladder"** in Hologram mode. Uses Ashley-QwQ's new
  single-lobe patterns (Disc Neo multi-view textures), each bright at one narrow angle and dark
  elsewhere, so each eye should see less of the other eye's view. Works with 2 to 7 views.
- **New, experimental: "Off areas: Dark".** Fills the dark parts of the picture with Ashley-QwQ's
  dark backgrounds, picking the one that stays dimmest where your views light up.
- **Every key file now says where to put the lamp,** worked out from the viewing distance. That
  should also help when you see no colour.
- **New test-patch disc:** classic patterns, ladder patterns, and ladder with a dark background,
  side by side on one ring, so one burn shows which works best on your discs.
- The ladder patterns are model predictions, checked on DVD-R but not yet photographed one by
  one, so burn the test-patch disc first.

## v1.08 (2026-11-26)

- **Fixed:** the app could crash with "1049 (0x0419) is an invalid culture identifier" (or a
  similar number) on PCs with a non-English keyboard layout, such as Russian. It happened after
  making a disc image. The image itself was fine. Thanks to SugatoS for reporting it (issue #3).

## v1.07 (2026-11-14)

- **Fixed:** presets made in Disc Image Studio 1.5.0 weren't recognised, so you had to calibrate
  your discs again. HoloStudio3D now reads them, and so does the bundled Disc Image Studio. Your
  presets from Disc Image Studio or disc-probe show up as "Yours: ..." in every disc section.
  Thanks to LTVA1 for reporting it (issue #2).
- A single preset that can't be used is now skipped and named, instead of hiding all of them.
- Saving presets in the bundled Disc Image Studio keeps the values that 1.5.0 added.

## v1.06 (2026-11-11)

- **New, experimental: "Right view pattern" setting** (Hologram mode, 2 views): +0%, +10% or
  +14%. Ashley-QwQ's photos showed the right picture leaking into the left eye as a red ghost.
  A stretched pattern shifts that ghost out of sight at the left eye, while the right eye sees
  its picture turn from green toward yellow. Works with the usual pattern pair.
- **New test-patch disc:** three rows of patches that compare +0%, +10% and +14% side by side on
  one disc, so a single burn shows which works best. The key file explains how to photograph it.
- **Fixed:** on the command line, `--test-patches`, `--off-fixed` and `--upright-bottom` swallowed
  the option after them.

## v1.05 (2026-11-11)

- Removed the Cube, F15 Jet and light bulb hologram samples from `samples/Sliicy`.

## v1.04 (2026-11-11)

- **Extra samples by Sliicy** in `samples/Sliicy`: stereograms and the results of Sliicy's own
  test discs, shared with permission. Sliicy is credited on the Info page and in the README.

## v1.03 (2026-11-10)

Improvements from Sliicy's feedback (issue #1):

- The mouse wheel always scrolls the page; it no longer changes sliders or lists by accident.
- "Hidden-3D picture" is now called "Magic Eye-style picture".
- Notes under sliders are no longer cut off.
- Sample files now come with the app (a torus-knot picture, depth map and model, a cube and test
  shapes), and the file boxes open there first.
- Your files are saved to `Documents\HoloStudio3D` by default, each design in its own folder.

## v1.02 (2026-11-09)

Fixes from Ashley-QwQ's review:

- **Fixed:** a 50% gray could come out fully on for one eye and fully off for the other. Each
  view now keeps the right brightness.
- **New, experimental: "Off areas" option.** Fill the dark parts with the original random mix, or
  with one pattern aimed away from both eyes.
- **New: test-patch disc,** a set of matched patches for a photo test of whether each eye really
  gets its own picture.

## v1.01 (2026-11-09)

- **Fixed:** in Stage 1 (measuring discs), a command that needs a correction file failed when
  that file didn't exist yet. It's now created first.
- The Stage 1 command box accepts commands copied with extra text in front of them.

## v1 (2026-11-09)

First release: Hologram mode (2 to 7 views from pictures, depth maps or 3D models), Stage 1 disc
measuring with disc-probe, Magic Eye-style pictures, and experimental rainbow and tilt-image
discs.
