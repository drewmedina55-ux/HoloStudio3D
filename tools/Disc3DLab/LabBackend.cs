#nullable disable
using System;
using System.Collections.Generic;
using System.Threading;

namespace Disc3DLab
{
    // Plain data passed between the window and the engine. Kept free of newer .NET APIs on
    // purpose, so the window code can also be compile-checked against other WinForms builds.

    public sealed class LabImage
    {
        public int Width;
        public int Height;
        public byte[] Bgra; // 4 bytes per pixel: blue, green, red, alpha
    }

    public sealed class StereoRequest
    {
        public string Source = "demo"; // demo | depth | mesh
        public string Demo = "torus";
        public string File;
        public bool Invert;
        public double Yaw;
        public double Pitch;
        public double Diameter = 70;
        public double Separation = 14;
        public double DepthFactor = 1.0 / 3;
        public double Dot = 0.15;
        public int Seed = 1;
        public bool Guides = true;
    }

    public sealed class TiltLayerRequest
    {
        public string File;
        public string Name;
        public string Grating;
    }

    public sealed class TiltRequest
    {
        public List<TiltLayerRequest> Layers = new List<TiltLayerRequest>();
        public bool Dither = true;
        public double Threshold = 0.5;
        public string Preset = "dvd-5-120mm";
    }

    public sealed class StereoPairRequest
    {
        public string Mode = "sbs"; // depth | model | frames | sbs | two
        public int ViewCount = 2;    // depth and model modes: how many views (2-7)
        public string[] FrameFiles;  // frames mode: 2-7 pictures, left-most view first
        public string ModelFile;     // model mode: .obj or .stl
        public double ModelYaw;      // model mode: starting turn, degrees
        public double ModelPitch;
        public double ModelTurn = 6; // model mode: how far the model turns between your two eyes, degrees
        public double ModelZoom = 1;
        public string SbsFile;
        public string LeftFile;
        public string RightFile;
        public string ColorFile;     // depth mode: the picture (RGB)
        public string DepthFile;     // depth mode: its depth map (white = near)
        public bool InvertDepth;     // depth map has black = near
        public double DepthStrength = 3;   // biggest left/right shift, % of picture width
        public double DepthFocus = 0.5;    // 0..1: this depth sits on the disc surface; nearer pops out
        public bool Squeezed;        // half-width side-by-side
        public bool CrossEyed;       // right-eye view on the left
        public double DistanceCm = 40;
        public string PairLeft;      // null = choose automatically
        public string PairRight;
        public bool SwapEyes;
        public double WidthMm = 50;
        public bool Large;              // let the picture run past the band; the disc edges clip it
        public double OffsetXMm;        // move the picture right (+) or left (-) on the disc, mm
        public double OffsetYMm;        // move the picture up (+, toward the rim at 12 o'clock) or down (-), mm
        public int Copies = 1;          // copies around the disc (1-8), each turned to face out from the centre
        public bool UprightBottom;      // 2 copies: the bottom copy stays upright, so both show at once
        public string OffMode = "mix";  // "off" cells: "mix" (random mix) or "fixed" (one pattern aimed away from the eyes)
        public int RightStretch;        // 2 views: right view's pattern period +0 / +10 / +14 % (only with the 66 pattern)
        public bool TestPatches;        // make the test-patch disc (all off / left / right / both) instead of a picture
        public double CellMm = 0.05;    // dither cell size: 0.05 fine, 0.1 normal, 0.2 coarse
        public bool Dither = true;
        public double Threshold = 0.5;
        public bool TestStrips = true;
        public string Preset = "dvd-5-120mm";
    }

    public sealed class PresetInfo
    {
        public string Id;
        public string Name;
        public override string ToString() => Name;
    }

    public sealed class GratingInfo
    {
        public string Code;
        public double Angle;
        public override string ToString() => Code + "   ·   about " + Angle.ToString("0.#") + "°";
    }

    public interface ILabBackend
    {
        IList<PresetInfo> Presets { get; }
        /// <summary>A line about where "Yours" presets come from, or why they couldn't be read.</summary>
        string PresetNote { get; }
        IList<GratingInfo> Gratings { get; }
        IList<string> TiltDefaults { get; }
        IList<string> Demos { get; }
        /// <summary>Pattern pairs on the stereo test tiles, as "LEFT,RIGHT" (tile 1 first).</summary>
        IList<string> StereoTestPairs { get; }
        string DefaultOutputFolder { get; }
        string SettingsFolder { get; }

        void RenderStereoPreview(StereoRequest request, int size, out LabImage stereogram, out LabImage depth);
        string SaveStereogram(StereoRequest request, string folder, string name);
        LabImage TestMap(string preset, out string key);
        LabImage TiltMap(TiltRequest request, out string key);

        /// <summary>Writes the full disc image. Returns the .iso path. Throws OperationCanceledException when cancelled.</summary>
        string MakeTestDisc(string preset, string folder, string name,
            Action<double, string> progress, Action<string> log, CancellationToken cancel);
        string MakeTiltDisc(TiltRequest request, string folder, string name,
            Action<double, string> progress, Action<string> log, CancellationToken cancel);

        /// <summary>One-line summary of the pattern pair that would be used, e.g. for the window.</summary>
        string DescribePair(StereoPairRequest request);
        LabImage StereoPairMap(StereoPairRequest request, out string key);
        string MakeStereoPairDisc(StereoPairRequest request, string folder, string name,
            Action<double, string> progress, Action<string> log, CancellationToken cancel);

        void ShowInFolder(string path);

        // ── Stage 1: measure your discs with disc-probe ──

        /// <summary>Null when disc-probe can run; otherwise a sentence saying why not.</summary>
        string ProbeProblem { get; }
        /// <summary>Where disc-probe keeps its results (its "results" folder lives here).</summary>
        string ProbeWorkFolder { get; }
        IList<string> OpticalDrives();
        /// <summary>Runs one disc-probe command and blocks until it ends. Whole lines go to <paramref name="line"/>;
        /// a progress line that rewrites itself goes to <paramref name="status"/>. Returns the exit code.</summary>
        int RunProbe(IList<string> args, Action<string> line, Action<string> status, CancellationToken cancel);
        /// <summary>Reads Disc Image Studio's preset files again (after installing a preset).</summary>
        void ReloadPresets();
    }
}
