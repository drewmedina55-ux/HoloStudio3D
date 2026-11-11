using DiscImageStudio.Core;
using DiscImageStudio.ImageIO;
using DvdImageSolver.Solver;

namespace Disc3DLab;

/// <summary>
/// A physical DVD layout, taken from the app's built-in DVD presets.
/// Grating periods depend only on the channel-bit length, not on calibration;
/// calibration only affects where shapes land, so the standard preset is fine for tests.
/// </summary>
internal sealed record DvdGeometry(string Id, string Name, uint Sectors, double InnerMm, double OuterMm,
    double BitNm, double PitchLinear, double PitchQuadratic, double PitchCubic)
{
    /// <summary>
    /// Your own presets first, then the app's built-in ones. "Yours" are the DVD presets in Disc Image
    /// Studio's user preset file (%LOCALAPPDATA%\DiscImageStudio\disc-presets.json): the ones you save in
    /// the app after calibrating, and the ones disc-probe installs with --install.
    /// </summary>
    public static IReadOnlyList<DvdGeometry> BuiltIn() => _all ??= Load();

    private static IReadOnlyList<DvdGeometry> _all;

    /// <summary>Why the user preset file couldn't be read, if it couldn't (shown in the window).</summary>
    public static string UserPresetProblem { get; private set; }

    public static string UserPresetPath => DiscPresetJsonStore.GetDefaultPath();

    /// <summary>Reads the preset files again (after installing a new preset).</summary>
    public static void Reload() => _all = null;

    private static IReadOnlyList<DvdGeometry> Load()
    {
        List<DvdGeometry> list = [];
        UserPresetProblem = null;
        try
        {
            string path = UserPresetPath;
            if (File.Exists(path))
                list.AddRange(DiscPresetJsonStore.Load(path).DvdPresets
                    .Where(p => p.OuterRadiusMm > 50 && p.Id != "user-custom-dvd") // skip the untouched template
                    .Select(p => From(p, "user:" + p.Id, "Yours: " + p.DisplayName)));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
            or System.Text.Json.JsonException or ArgumentException or InvalidOperationException or FormatException)
        {
            UserPresetProblem = "Couldn't read your presets file (" + exception.Message + "). Using the built-in presets.";
        }
        list.AddRange(DiscPresetJsonStore.LoadBuiltIn().DvdPresets
            .Where(p => p.OuterRadiusMm > 50) // 12 cm discs only
            .Select(p => From(p, p.Id, p.DisplayName)));
        return list;
    }

    private static DvdGeometry From(DvdDiscPresetDefinition p, string id, string name) =>
        // Disc Image Studio always generates DVDs at the standard 133.3 nm channel bit and keeps the
        // file's value only for compatibility; do the same so a preset places pictures identically.
        new(id, name, p.TotalSectors, p.InnerRadiusMm, p.OuterRadiusMm, 133.3, p.PitchLinear, p.PitchQuadratic, p.PitchCubic);

    public static DvdGeometry Find(string id) => BuiltIn().FirstOrDefault(g => g.Id == id)
        ?? throw new ArgumentException($"Unknown DVD preset '{id}'. Available: {string.Join(", ", BuiltIn().Select(g => g.Id))}");

    public MultiBlockSolveOptions Options(uint start, uint fill, int parallelism) => new(
        start, fill, 0x30000, 0, 1, 1, 0, 0, true,
        new ImageMappingOptions(Sectors, InnerMm, OuterMm, BitNm, 0, true, 128, 1,
            PitchLinear: PitchLinear, PitchQuadratic: PitchQuadratic, PitchCubic: PitchCubic),
        Algorithm: SolverAlgorithm.Dispersion, FastParallelism: parallelism);
}

/// <summary>
/// One "grating" texture. Each DVD byte becomes a 16-channel-bit EFMPlus code word; a run of
/// the same byte repeats a fixed mark pattern along the track, which is a diffraction grating.
/// Its strongest period sets the angle at which it throws coloured light.
/// Steady-state periods below were computed from the ECMA-267 EFMPlus tables (bytes >= 0x58
/// only, so the drive's DSV substitution table never changes the pattern).
/// </summary>
internal sealed record Grating(string Code, string Label, double PeriodChannelBits, Func<ulong, byte> ByteAt)
{
    public double PeriodUm(double bitNm) => PeriodChannelBits * bitNm / 1000;

    /// <summary>First-order diffraction angle from the mirror reflection, for light hitting the disc straight on.</summary>
    public double AngleDegrees(double wavelengthNm, double bitNm)
    {
        double s = wavelengthNm / 1000 / PeriodUm(bitNm);
        return s >= 1 ? double.NaN : Math.Asin(s) * 180 / Math.PI;
    }

    public static Grating Constant(byte value, double period) =>
        new($"{value:X2}", $"constant {value:X2}", period, _ => value);

    public static Grating Alternating(byte a, byte b, int bytesPerHalf) =>
        new($"{a:X2}/{b:X2}x{bytesPerHalf}", $"{a:X2}/{b:X2} alternating every {bytesPerHalf} byte(s)",
            32.0 * bytesPerHalf, index => (index / (ulong)bytesPerHalf) % 2 == 0 ? a : b);

    public static readonly byte[] Palette = [0xA5, 0x66, 0xB9, 0x92];

    /// <summary>Aperiodic reference: a random mix of the four gray-palette bytes. Same average look,
    /// no single grating period, so it should show only a weak, smeared rainbow.</summary>
    public static readonly Grating Random = new("RND", "random mix (no grating, reference)", double.NaN, index =>
    {
        ulong h = index * 0x9E3779B97F4A7C15UL;
        h ^= h >> 31; h *= 0xBF58476D1CE4E5B9UL; h ^= h >> 29;
        return Palette[(int)(h & 3)];
    });

    /// <summary>Test-disc rings, inside to outside. Periods in channel bits (1 T = 133.3 nm).</summary>
    public static readonly Grating[] TestRings =
    [
        Constant(0xA5, 6.4),     // ~40 deg green
        Constant(0x9C, 7.11),    // ~36 deg
        Constant(0x66, 8.0),     // ~31 deg
        Constant(0xCE, 9.14),    // ~27 deg
        Constant(0xB9, 10.67),   // ~23 deg
        Constant(0x9A, 12.8),    // ~19 deg
        Constant(0x92, 16.0),    // ~15 deg
        Constant(0x93, 21.33),   // ~11 deg (weaker)
        Alternating(0xA5, 0x92, 1), // 32 T ~7.4 deg
        Alternating(0xA5, 0x92, 2), // 64 T ~3.7 deg
        Alternating(0xA5, 0x92, 4), // 128 T ~1.8 deg
        Random,
    ];

    /// <summary>Tilt-image layers: the four strongest, cleanest gratings, spread across angles.</summary>
    public static readonly Grating[] TiltLayers =
    [
        Constant(0x92, 16.0),   // shows first, at the smallest tilt (~15 deg)
        Constant(0xB9, 10.67),  // ~23 deg
        Constant(0x66, 8.0),    // ~31 deg
        Constant(0xA5, 6.4),    // ~40 deg, the steepest tilt
    ];

    /// <summary>A repeating run of bytes whose marks together form one grating (see <see cref="Stretched"/>).</summary>
    public static Grating Sequence(string code, string label, double period, byte[] bytes) =>
        new(code, label, period, index => bytes[(int)(index % (ulong)bytes.Length)]);

    /// <summary>
    /// 66 with a 10 % longer period (8.8 T instead of 8 T). No single byte gives that period, so this is
    /// an 11-byte run (176 channel bits = exactly 20 periods), found by searching the ECMA-267 EFMPlus
    /// main table (bytes >= 0x58 only) for the sequence whose marks put the most energy into 8.8 T:
    /// about 81 % in that one period, close to plain 66 (85 %) and cleaner than CE (74 %).
    /// </summary>
    public static readonly Grating Stretch66By10 = Sequence("66+10", "66 stretched 10 % (11-byte run)", 8.8,
        [0xBB, 0x7D, 0x98, 0xBF, 0x67, 0x81, 0xC6, 0x67, 0xCC, 0xFC, 0x58]);

    /// <summary>The right-view stretches that exist, in percent. +14 is plain CE (9.14 T vs 66's 8 T).</summary>
    public static readonly int[] Stretches = [0, 10, 14];

    /// <summary>
    /// The pattern with a longer period, so its colours move toward red (Ashley-QwQ's fix for the
    /// right view leaking into the left eye). Only 66 has stretched versions; null for anything else.
    /// </summary>
    public static Grating Stretched(Grating g, int percent) => percent switch
    {
        0 => g,
        10 when g.Code == "66" => Stretch66By10,
        14 when g.Code == "66" => FromCode("CE"),
        _ => null,
    };

    public static Grating FromCode(string code)
    {
        if (string.Equals(code, Stretch66By10.Code, StringComparison.OrdinalIgnoreCase)) return Stretch66By10;
        foreach (Grating g in TestRings) if (string.Equals(g.Code, code, StringComparison.OrdinalIgnoreCase)) return g;
        throw new ArgumentException($"Unknown grating '{code}'. Use one of: {string.Join(", ", TestRings.Select(g => g.Code))}");
    }
}

/// <summary>Layout in disc millimetres (x right, y down, centre 0,0) mapped to a grating per point.</summary>
internal interface IDiffractionLayout
{
    /// <summary>Index into <see cref="Gratings"/> for this point.</summary>
    int GratingAt(double xMm, double yMm);
    IReadOnlyList<Grating> Gratings { get; }
    string Describe(DvdGeometry geometry);
}

/// <summary>Rings of test gratings; every other 45 deg segment is the random reference for comparison.</summary>
internal sealed class TestDiscLayout : IDiffractionLayout
{
    public const double FirstRadius = 24.6, RingPitch = 2.72, Gap = .32;
    private readonly LabelMask _labels = new();
    private readonly int _random;

    public TestDiscLayout()
    {
        _random = Array.IndexOf(Grating.TestRings, Grating.Random);
        Gratings = [.. Grating.TestRings, Grating.Constant(0xA5, 6.4), Grating.Constant(0x92, 16.0)];
        for (int ring = 0; ring < Grating.TestRings.Length; ring++)
        for (int segment = 1; segment < 8; segment += 2)
        {
            double angle = (segment + .5) * Math.PI / 4;
            double radius = FirstRadius + ring * RingPitch + (RingPitch - Gap) / 2;
            _labels.Draw((ring + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), angle, radius, .16);
        }
        // Orientation mark at 12 o'clock just outside the last ring: "N" for north.
        _labels.Draw("N", 0, FirstRadius + Grating.TestRings.Length * RingPitch + .9, .14);
    }

    public IReadOnlyList<Grating> Gratings { get; }

    public int GratingAt(double x, double y)
    {
        int label = _labels.At(x, y);
        if (label == 1) return Gratings.Count - 2; // label ink: A5 (darkest)
        if (label == 2) return Gratings.Count - 1; // label paper: 92 (lightest)
        double r = Math.Sqrt(x * x + y * y);
        double offset = r - FirstRadius;
        if (offset < 0) return _random;
        int ring = (int)(offset / RingPitch);
        if (ring >= Grating.TestRings.Length) return _random;
        if (offset - ring * RingPitch >= RingPitch - Gap) return _random;
        double angle = Math.Atan2(x, -y);
        if (angle < 0) angle += 2 * Math.PI;
        int segment = (int)(angle / (Math.PI / 4));
        return (segment & 1) == 0 ? ring : _random;
    }

    public string Describe(DvdGeometry g)
    {
        var lines = new List<string>
        {
            "DIFFRACTION TEST DISC - KEY",
            $"Disc layout preset: {g.Name} ({g.Id}), {g.Sectors:N0} sectors, channel bit {g.BitNm} nm",
            "",
            "Rings are numbered 1 (innermost) to 12 (outermost); numbers are printed in each reference segment.",
            "Around each ring, segments alternate every 45 degrees: test grating, then random reference.",
            "The 'N' mark sits at 12 o'clock just outside ring 12.",
            "",
            "Ring  Texture                                   Period      First-order angle (blue 450 / green 550 / red 650 nm)",
        };
        for (int i = 0; i < Grating.TestRings.Length; i++)
        {
            Grating gr = Grating.TestRings[i];
            double r0 = FirstRadius + i * RingPitch;
            string period = double.IsNaN(gr.PeriodChannelBits) ? "none" : $"{gr.PeriodUm(g.BitNm):0.00} um";
            string angles = double.IsNaN(gr.PeriodChannelBits) ? "-" : string.Join(" / ",
                new[] { 450.0, 550, 650 }.Select(w => gr.AngleDegrees(w, g.BitNm)).Select(a => double.IsNaN(a) ? "--" : $"{a:0.0}"));
            lines.Add($"{i + 1,4}  {gr.Label,-40}  {period,-10}  {angles}   (r {r0:0.0}-{r0 + RingPitch - Gap:0.0} mm)");
        }
        lines.AddRange(
        [
            "",
            "HOW TO LOOK",
            "1. One small bright light (phone flashlight, bare LED, or sun). Dark room is best.",
            "2. Turn the disc so the area you're checking is at 12 or 6 o'clock: the tracks there run left-right,",
            "   and these gratings throw light sideways along the track (left-right), not toward/away from you.",
            "3. Start at the mirror reflection of the light, then tilt the disc slowly left and right.",
            "   A working grating ring lights up in a clean colour band that moves through the rainbow as you tilt,",
            "   at roughly the angle in the table. The random segments next to it should stay dull or smeared.",
            "4. Ignore the big rainbow that runs from the centre outward: that's the normal DVD track pitch.",
            "",
            "Report back which rings show a clean colour that the random segments don't. The bright rings become",
            "the 'inks' for tilt images.",
        ]);
        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>Up to four pictures, each drawn with its own grating, so each appears at its own tilt angle.</summary>
internal sealed class TiltImageLayout : IDiffractionLayout
{
    private const double CellMm = .1;
    private const int Cells = 1200; // 120 mm canvas
    private readonly byte[] _cellLayer; // 0 = background, k = layer k
    private readonly int _background;

    public TiltImageLayout(IReadOnlyList<string> images, IReadOnlyList<Grating> layers, double threshold, bool dither,
        IReadOnlyList<string>? displayNames = null)
    {
        if (images.Count is < 1 or > 4) throw new ArgumentException("Use one to four images.");
        if (layers.Count < images.Count) throw new ArgumentException("Each image needs a grating.");
        Gratings = [.. layers.Take(images.Count), Grating.Random];
        _background = Gratings.Count - 1;
        Images = images;
        Names = displayNames ?? images.Select(Path.GetFileName).Select(n => n ?? "picture").ToList();

        bool[][] masks = images.Select(path => Mask(path, threshold, dither)).ToArray();
        _cellLayer = new byte[Cells * Cells];
        Span<int> on = stackalloc int[4];
        for (int y = 0; y < Cells; y++)
        for (int x = 0; x < Cells; x++)
        {
            int index = y * Cells + x;
            int count = 0;
            for (int k = 0; k < masks.Length; k++) if (masks[k][index]) on[count++] = k;
            // Overlapping pictures share the spot in a fine checkerboard of their gratings.
            _cellLayer[index] = count == 0 ? (byte)0 : (byte)(on[(x + y * 3) % count] + 1);
        }
    }

    public IReadOnlyList<string> Images { get; }
    public IReadOnlyList<string> Names { get; }
    public IReadOnlyList<Grating> Gratings { get; }

    public int GratingAt(double x, double y)
    {
        int cx = (int)Math.Floor((x + 60) / CellMm), cy = (int)Math.Floor((y + 60) / CellMm);
        if (cx < 0 || cy < 0 || cx >= Cells || cy >= Cells) return _background;
        int layer = _cellLayer[cy * Cells + cx];
        return layer == 0 ? _background : layer - 1;
    }

    /// <summary>Picture to on/off cells. Bright = drawn. "Contain" fit to the 120 mm canvas.</summary>
    private static bool[] Mask(string path, double threshold, bool dither)
    {
        BgraRaster raster = RasterCodec.LoadBgra32(path, 512L * 1024 * 1024, 200_000_000);
        double scale = Math.Min((double)Cells / raster.Width, (double)Cells / raster.Height);
        double ox = (Cells - raster.Width * scale) / 2, oy = (Cells - raster.Height * scale) / 2;
        double[] value = new double[Cells * Cells];
        for (int y = 0; y < Cells; y++)
        for (int x = 0; x < Cells; x++)
        {
            int sx = (int)Math.Floor((x + .5 - ox) / scale), sy = (int)Math.Floor((y + .5 - oy) / scale);
            if (sx < 0 || sy < 0 || sx >= raster.Width || sy >= raster.Height) continue;
            int o = (sy * raster.Width + sx) * 4;
            double lum = (.0722 * raster.Pixels[o] + .7152 * raster.Pixels[o + 1] + .2126 * raster.Pixels[o + 2]) / 255.0;
            value[y * Cells + x] = lum * raster.Pixels[o + 3] / 255.0;
        }
        bool[] mask = new bool[Cells * Cells];
        if (!dither)
        {
            for (int i = 0; i < mask.Length; i++) mask[i] = value[i] >= threshold;
            return mask;
        }
        // Floyd-Steinberg: lets grays come through as partial coverage of the grating.
        for (int y = 0; y < Cells; y++)
        for (int x = 0; x < Cells; x++)
        {
            int i = y * Cells + x;
            bool on = value[i] >= .5;
            mask[i] = on;
            double error = value[i] - (on ? 1 : 0);
            if (x + 1 < Cells) value[i + 1] += error * 7 / 16;
            if (y + 1 < Cells)
            {
                if (x > 0) value[i + Cells - 1] += error * 3 / 16;
                value[i + Cells] += error * 5 / 16;
                if (x + 1 < Cells) value[i + Cells + 1] += error / 16;
            }
        }
        return mask;
    }

    public string Describe(DvdGeometry g)
    {
        var lines = new List<string>
        {
            "TILT IMAGE DISC - KEY",
            $"Disc layout preset: {g.Name} ({g.Id}), {g.Sectors:N0} sectors",
            "",
            "Layer  Picture                                   Grating     Lights up near (blue / green / red)",
        };
        for (int i = 0; i < Images.Count; i++)
        {
            Grating gr = Gratings[i];
            string angles = string.Join(" / ", new[] { 450.0, 550, 650 }.Select(w => gr.AngleDegrees(w, g.BitNm)).Select(a => double.IsNaN(a) ? "--" : $"{a:0.0}"));
            lines.Add($"{i + 1,5}  {Names[i],-40}  {gr.Code,-10}  {angles} deg");
        }
        lines.AddRange(
        [
            "",
            "Background: random reference mix (no grating).",
            "Look with one small bright light. Pictures show best at 12 and 6 o'clock, where the tracks run",
            "left-right; tilt the disc left/right away from the mirror reflection. Each picture flashes in colour",
            "at its own tilt. Near 3 and 9 o'clock the effect turns into a forward/back tilt instead.",
        ]);
        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>Small bitmap labels (5x7 font) drawn into a 0.04 mm grid.</summary>
internal sealed class LabelMask
{
    private const double CellMm = .04;
    private const int Cells = 3000;
    private readonly byte[] _mask = new byte[Cells * Cells]; // 0 none, 1 ink, 2 paper

    internal static readonly Dictionary<char, string> Font = new()
    {
        ['0'] = "01110/10001/10011/10101/11001/10001/01110", ['1'] = "00100/01100/00100/00100/00100/00100/01110",
        ['2'] = "01110/10001/00001/00010/00100/01000/11111", ['3'] = "11110/00001/00001/01110/00001/00001/11110",
        ['4'] = "00010/00110/01010/10010/11111/00010/00010", ['5'] = "11111/10000/10000/11110/00001/00001/11110",
        ['6'] = "01110/10000/10000/11110/10001/10001/01110", ['7'] = "11111/00001/00010/00100/01000/01000/01000",
        ['8'] = "01110/10001/10001/01110/10001/10001/01110", ['9'] = "01110/10001/10001/01111/00001/00001/01110",
        ['N'] = "10001/11001/10101/10011/10001/10001/10001",
        ['L'] = "10000/10000/10000/10000/10000/10000/11111", ['R'] = "11110/10001/10001/11110/10100/10010/10001",
    };

    public int At(double x, double y)
    {
        int ix = (int)Math.Floor((x + 60) / CellMm), iy = (int)Math.Floor((y + 60) / CellMm);
        return ix >= 0 && iy >= 0 && ix < Cells && iy < Cells ? _mask[iy * Cells + ix] : 0;
    }

    /// <summary>Text centred at (radius, angle from 12 o'clock clockwise), baseline tangent to the circle.</summary>
    public void Draw(string text, double angle, double radius, double dotMm)
    {
        double width = text.Length * 6 * dotMm;
        double sine = Math.Sin(angle), cosine = Math.Cos(angle);
        for (double dv = -4.5 * dotMm; dv < 4.5 * dotMm; dv += CellMm / 2)
        for (double u = -width / 2 - dotMm; u < width / 2 + dotMm; u += CellMm / 2)
        {
            int gx = (int)Math.Floor((u + width / 2) / dotMm);
            int gy = (int)Math.Floor(3.5 - dv / dotMm);
            bool ink = gx >= 0 && gx / 6 < text.Length && gx % 6 < 5 && gy >= 0 && gy < 7
                && Font[text[gx / 6]][gy * 6 + gx % 6] == '1';
            double x = (radius + dv) * sine + u * cosine, y = -(radius + dv) * cosine + u * sine;
            int ix = (int)Math.Floor((x + 60) / CellMm), iy = (int)Math.Floor((y + 60) / CellMm);
            if (ix >= 0 && iy >= 0 && ix < Cells && iy < Cells) _mask[iy * Cells + ix] = ink ? (byte)1 : (byte)2;
        }
    }
}
