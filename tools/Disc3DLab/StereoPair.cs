#nullable disable
using DiscImageStudio.ImageIO;

namespace Disc3DLab;

/// <summary>
/// Hologram mode (the stereo pair, generalised to 2-7 views). The views are interleaved cell by
/// cell, each drawn with its own along-track grating. Each grating sends its light out at its own
/// angle, so from any one spot an eye catches one view; your two eyes, a few degrees apart, catch
/// two different views, and moving your head brings the neighbouring views in, like a lenticular.
/// The tracks only run left-right near 12 and 6 o'clock, so the picture sits at 12 o'clock and the
/// L/R test tiles sit at 6 o'clock (upside down, so they read upright when you turn the disc).
/// </summary>
internal sealed class StereoPairLayout : IDiffractionLayout
{
    public const double EyeSeparationCm = 6.5;
    public const int MaxViews = 7;
    private readonly double _cell;      // dither cell size in mm (Detail setting)
    private readonly bool _large;       // picture may run past the band and get clipped by the disc edges
    private readonly double _offsetX, _offsetY; // where the picture was moved to, mm (right, up)
    private readonly int _copies;       // copies around the disc
    private readonly bool _uprightBottom;
    private readonly int _off;          // grating index used for "off" cells
    private readonly bool _offFixed;    // off cells use one fixed pattern aimed away from the eyes
    private readonly Grating _offGrating;
    private readonly List<(double X, double Y, int Kind, int Row, int Number)> _patches; // test-patch disc
    private readonly int[] _patchRight;          // right-view pattern of each patch row: +0 / +10 / +14 %
    private readonly int _stretch;               // right-view stretch actually used, percent
    private readonly int _stretchAsked;

    // Gratings with one clean period. The four gray-palette bytes are the strongest.
    private static readonly string[] Clean = ["A5", "9C", "66", "CE", "B9", "9A", "92"];
    private static readonly string[] Strong = ["A5", "66", "B9", "92"];

    public static readonly (string Left, string Right)[] TestPairs =
        [("B9", "92"), ("66", "B9"), ("A5", "66"), ("CE", "9A"), ("66", "92")];

    private readonly bool[][] _views;              // dithered "on" cells of each view inside the picture box
    private readonly double[][] _previews;         // small copies of the views, for -views.png
    private readonly int _previewW, _previewH;
    private readonly int _nx, _ny, _n, _stride;
    private readonly double _boxX, _boxY, _boxW, _boxH;
    private readonly int _random;
    private readonly List<(double X, double Y, int Left, int Right, int Pair)> _tiles = [];
    private readonly LabelMask _labels = new();
    private readonly int _labelInk, _labelPaper;
    private readonly bool _testTiles;
    private readonly double _patchSize;

    public StereoPairLayout(StereoPairRequest r)
    {
        DistanceCm = r.DistanceCm;
        _testTiles = r.TestStrips || r.TestPatches;
        if (r.TestPatches)
        {
            (Grating left, Grating right) = ChoosePair(r);
            Views = [left, right];
        }
        else Views = ChooseViews(r);
        _n = Views.Length;
        _stretch = _n == 2 && !r.TestPatches && Grating.Stretched(ChoosePair(r).Right, r.RightStretch) != null ? r.RightStretch : 0;
        _stretchAsked = _n == 2 && !r.TestPatches ? r.RightStretch : 0;
        PairLeft = Views[0]; PairRight = Views[^1];
        // Spread the views evenly: neighbouring cells in a row always belong to different views.
        _stride = _n == 2 ? 1 : _n >= 4 ? 2 : 1;

        (double aspect, string[] names, Func<int, double[]> view) = r.TestPatches
            ? (1.0, ["left eye", "right eye"], _ => [0, 1, 1]) // no picture on the test-patch disc
            : PlanViews(r, Positions(r, Views));
        Names = names;

        _cell = Math.Clamp(r.CellMm, .03, .3);
        _large = r.Large;
        double width, height, centerY;
        if (_large)
        {
            // Large: centre the picture on the middle of the readable band at 12 o'clock (41 mm out),
            // where the subject of a picture usually is. Whatever falls past the outer edge or over
            // the centre hole is simply left out.
            width = Math.Clamp(r.WidthMm, 15, 116);
            height = width / aspect;
            if (height > 80) { height = 80; width = height * aspect; } // stays clear of the test tiles
            centerY = 41;
        }
        else
        {
            // Fit the whole picture box in the 12 o'clock band: above the hole, inside the outer edge.
            width = Math.Clamp(r.WidthMm, 15, 80); height = width / aspect;
            if (height > 30) { height = 30; width = height * aspect; }
            while (true)
            {
                centerY = 25.6 + height / 2;                     // distance from centre to the box middle
                double top = centerY + height / 2;
                if (Math.Sqrt(width * width / 4 + top * top) <= 57.3 || width < 10) break;
                width *= .97; height *= .97;
            }
        }
        // Position: slide the picture left/right and up/down. Whatever ends up past the rim or over
        // the hole is left out.
        _offsetX = Math.Clamp(r.OffsetXMm, -40, 40); _offsetY = Math.Clamp(r.OffsetYMm, -30, 30);
        _copies = Math.Clamp(r.Copies, 1, 8);
        _uprightBottom = _copies == 2 && r.UprightBottom;
        _boxW = width; _boxH = height; _boxX = -width / 2 + _offsetX; _boxY = -centerY - height / 2 - _offsetY;
        WidthMm = width; HeightMm = height;
        _nx = Math.Max(1, (int)Math.Round(width / _cell)); _ny = Math.Max(1, (int)Math.Round(height / _cell));

        // One view at a time, so even 7 views stay light on memory.
        _views = new bool[_n][];
        _previews = new double[_n][];
        _previewW = Math.Min(900 / Math.Max(2, _n) * 2, 900);
        _previewH = Math.Max(1, (int)Math.Round(_previewW / aspect));
        for (int i = 0; i < _n; i++)
        {
            double[] grid = view(i);
            _views[i] = DitherView(Resample(grid, _nx, _ny), _nx, _n, _stride, i, r.Dither, r.Threshold);
            _previews[i] = Resample(grid, _previewW, _previewH);
        }

        List<Grating> gratings = [.. Views, Grating.Random];
        _random = _n;
        // "Off" cells: the random mix (still contains the view patterns, so it isn't truly dark for
        // the eyes), or one fixed pattern aimed well away from every view (Ashley-QwQ's suggestion:
        // 92, or the clean pattern farthest from the views if a view already uses 92).
        _offGrating = FixedOff(Views);
        _offFixed = r.OffMode == "fixed";
        _off = _random;
        if (_offFixed && !r.TestPatches)
        {
            gratings.Add(_offGrating);
            _off = gratings.Count - 1;
        }
        if (r.TestPatches)
        {
            // Matched patches: all off / left only / right only / both on, in three rows that differ only
            // in the right view's pattern: +0 % (outer), +10 % and +14 % (inner), all with the random-mix
            // off texture (Ashley-QwQ: the fixed off pattern wasn't darker).
            _patchRight = new int[Grating.Stretches.Length];
            for (int row = 0; row < _patchRight.Length; row++)
            {
                Grating stretched = Grating.Stretched(PairRight, Grating.Stretches[row]);
                if (stretched == null || row == 0) { _patchRight[row] = 1; continue; }
                gratings.Add(stretched);
                _patchRight[row] = gratings.Count - 1;
            }
            _patches = [];
            const double size = 6, gap = 2, rowPitch = 9;
            double rowWidth = 4 * size + 3 * gap;
            for (int row = 0; row < _patchRight.Length; row++)
            for (int k = 0; k < 4; k++)
            {
                double cx0 = -rowWidth / 2 + k * (size + gap) + size / 2;
                double cy0 = -50 + row * rowPitch;
                _patches.Add((cx0, cy0, k, row, row * 4 + k + 1));
            }
            _patchSize = size;
            foreach ((double px0, double py0, int _, int _, int number) in _patches)
            {
                double angle = Math.Atan2(px0, -py0), radius = Math.Sqrt(px0 * px0 + py0 * py0);
                // Each number sits just outside its patch (toward the rim), in the gap between rows.
                _labels.Draw(number.ToString(System.Globalization.CultureInfo.InvariantCulture), angle,
                    radius + size / 2 + 1.3, .18);
            }
        }
        if (_testTiles)
        {
            // Five L/R tiles around 6 o'clock, rotated 180° so they read upright when turned to the top.
            for (int i = 0; i < TestPairs.Length; i++)
            {
                gratings.Add(Grating.FromCode(TestPairs[i].Left));
                gratings.Add(Grating.FromCode(TestPairs[i].Right));
                double angle = Math.PI + (i - 2) * 0.27;      // radians from 12 o'clock, clockwise
                double radius = 44;
                double x = radius * Math.Sin(angle), y = -radius * Math.Cos(angle);
                _tiles.Add((x, y, gratings.Count - 2, gratings.Count - 1, i + 1));
                _labels.Draw((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), angle, 36.6, .2);
            }
            gratings.Add(Grating.Constant(0xA5, 6.4));
            gratings.Add(Grating.Constant(0x92, 16.0));
            _labelInk = gratings.Count - 2; _labelPaper = gratings.Count - 1;
        }
        Gratings = gratings;
    }

    public IReadOnlyList<Grating> Gratings { get; }
    /// <summary>The view patterns, left-most view first.</summary>
    public Grating[] Views { get; }
    public string[] Names { get; }
    public Grating PairLeft { get; }
    public Grating PairRight { get; }
    public double DistanceCm { get; }
    public double WidthMm { get; }
    public double HeightMm { get; }

    // The picture's part of the disc: outside the clamping area, inside the outer edge.
    private static bool IsReadable(double x, double y)
    {
        double r = Math.Sqrt(x * x + y * y);
        return r >= 24.2 && r <= 57.9;
    }

    /// <summary>Working width of each view; enough for a 116 mm picture at the finest detail.</summary>
    private const int ViewCells = 2400;

    public static double TargetSeparation(double distanceCm) => Math.Atan(EyeSeparationCm / Math.Max(10, distanceCm)) * 180 / Math.PI;

    private static double Green(string code) => Grating.FromCode(code).AngleDegrees(550, 133.3);

    /// <summary>How many views a request makes.</summary>
    public static int ViewCount(StereoPairRequest r)
    {
        if (r.Mode == "frames")
        {
            int count = r.FrameFiles?.Length ?? 0;
            if (count < 2) throw new ArgumentException("Choose at least 2 pictures (up to 7), left-most view first.");
            if (count > MaxViews) throw new ArgumentException($"Up to {MaxViews} pictures: there are only {MaxViews} clean patterns.");
            return count;
        }
        if (r.Mode == "depth" || r.Mode == "model") return Math.Clamp(r.ViewCount, 2, MaxViews);
        return 2;
    }

    /// <summary>Picks the grating pair whose green angles differ by about the angle between your eyes.</summary>
    public static (Grating Left, Grating Right) ChoosePair(StereoPairRequest r)
    {
        Grating left, right;
        if (!string.IsNullOrEmpty(r.PairLeft) && !string.IsNullOrEmpty(r.PairRight))
        {
            left = Grating.FromCode(r.PairLeft);
            right = Grating.FromCode(r.PairRight);
        }
        else
        {
            double target = TargetSeparation(r.DistanceCm);
            var best = Clean.SelectMany(a => Clean, (a, b) => (a, b))
                .Select(p => (p.a, p.b, sa: Green(p.a), sb: Green(p.b)))
                .Where(p => p.sa > p.sb) // left eye gets the steeper angle by default
                .OrderBy(p => Math.Abs(p.sa - p.sb - target) + (Strong.Contains(p.a) && Strong.Contains(p.b) ? 0 : 1.5))
                .First();
            left = Grating.FromCode(best.a);
            right = Grating.FromCode(best.b);
        }
        return r.SwapEyes ? (right, left) : (left, right);
    }

    /// <summary>
    /// The view patterns, left-most view first. Two views use the eye pair. More views take evenly
    /// spaced clean patterns whose step is as close as possible to the angle between your eyes, so
    /// your eyes land on neighbouring views (or on views a couple apart when the steps are small).
    /// </summary>
    public static Grating[] ChooseViews(StereoPairRequest r)
    {
        int n = ViewCount(r);
        if (n == 2)
        {
            (Grating left, Grating right) = ChoosePair(r);
            return [left, Grating.Stretched(right, r.RightStretch) ?? right];
        }
        double target = TargetSeparation(r.DistanceCm);
        string[] steepFirst = Clean.OrderByDescending(Green).ToArray();
        string[] best = null;
        double bestScore = double.MaxValue;
        for (int stride = 1; stride <= 3; stride++)
        for (int start = 0; start + (n - 1) * stride < steepFirst.Length; start++)
        {
            string[] codes = Enumerable.Range(0, n).Select(i => steepFirst[start + i * stride]).ToArray();
            double step = (Green(codes[0]) - Green(codes[^1])) / (n - 1);
            // Prefer a step near the eye angle, the strong patterns, and the middle of the range.
            double score = Math.Abs(step - target) + .4 * codes.Count(c => !Strong.Contains(c)) / n;
            if (score < bestScore) { bestScore = score; best = codes; }
        }
        Grating[] views = best.Select(Grating.FromCode).ToArray();
        if (r.SwapEyes) Array.Reverse(views);
        return views;
    }

    /// <summary>Where each view is seen from, in eye-separations (0 = straight ahead).</summary>
    private static double[] Positions(StereoPairRequest r, Grating[] views)
    {
        int n = views.Length;
        if (n == 2) return [-.5, .5];
        double target = TargetSeparation(r.DistanceCm);
        double step = Math.Abs(views[0].AngleDegrees(550, 133.3) - views[^1].AngleDegrees(550, 133.3)) / (n - 1) / target;
        return Enumerable.Range(0, n).Select(i => (i - (n - 1) / 2.0) * step).ToArray();
    }

    public int GratingAt(double x, double y)
    {
        int cx = (int)Math.Floor((x + 60) / _cell), cy = (int)Math.Floor((y + 60) / _cell);

        // The test tiles sit on top of everything (with copies, they may cover part of the bottom copy).
        if (_testTiles)
        {
            int tile = TileAt(x, y, cx, cy);
            if (tile >= 0) return tile;
        }
        if (!IsReadable(x, y)) return _random;
        if (_patches != null) return PatchAt(x, y);

        // Copies: the disc is cut into equal slices and each slice shows the picture turned to face
        // out from the centre, so whichever copy is turned to 12 o'clock is upright, with its tracks
        // running left-right. With 2 copies the bottom one can instead stay upright (moved, not turned).
        double px = x, py = y;
        if (_uprightBottom)
        {
            if (y > 0) py = y + 2 * (_boxY + _boxH / 2);
        }
        else if (_copies > 1)
        {
            double slice = 2 * Math.PI / _copies;
            double angle = Math.Atan2(x, -y);                     // clockwise from 12 o'clock
            double turn = Math.Round(angle / slice) * slice;
            double radius = Math.Sqrt(x * x + y * y), a = angle - turn;
            px = radius * Math.Sin(a); py = -radius * Math.Cos(a);
        }

        if (px >= _boxX && px < _boxX + _boxW && py >= _boxY && py < _boxY + _boxH)
        {
            int ix = Math.Min(_nx - 1, (int)((px - _boxX) / _boxW * _nx)), iy = Math.Min(_ny - 1, (int)((py - _boxY) / _boxH * _ny));
            int view = ((ix + _stride * iy) % _n + _n) % _n; // 2 views: a checkerboard
            return _views[view][iy * _nx + ix] ? view : _off;
        }
        return _offFixed ? _off : _random;
    }

    /// <summary>
    /// The "off" pattern aimed away from the eyes: 92 (~15° green), or, if a view already uses 92,
    /// the clean pattern whose angle is farthest from every view.
    /// </summary>
    internal static Grating FixedOff(Grating[] views)
    {
        if (!views.Any(v => v.Code == "92")) return Grating.FromCode("92");
        string best = Clean.Concat(["93"]).Where(c => !views.Any(v => v.Code == c))
            .OrderByDescending(c => views.Min(v => Math.Abs(Green(c) - v.AngleDegrees(550, 133.3))))
            .First();
        return Grating.FromCode(best);
    }

    /// <summary>The test-patch disc: which pattern a point in (or around) the patches gets.</summary>
    private int PatchAt(double x, double y)
    {
        int cx = (int)Math.Floor((x + 60) / _cell), cy = (int)Math.Floor((y + 60) / _cell);
        bool leftCell = ((cx + cy) & 1) == 0;
        foreach ((double px0, double py0, int kind, int row, int _) in _patches)
        {
            if (Math.Abs(x - px0) > _patchSize / 2 || Math.Abs(y - py0) > _patchSize / 2) continue;
            bool on = kind == 3 || (kind == 1 && leftCell) || (kind == 2 && !leftCell);
            return on ? (leftCell ? 0 : _patchRight[row]) : _random;
        }
        return _random;
    }

    /// <summary>The test tile or label pattern at this point, or -1 if there is none.</summary>
    private int TileAt(double x, double y, int cx, int cy)
    {

        bool leftCell = ((cx + cy) & 1) == 0;
        int label = _labels.At(x, y);
        if (label == 1) return _labelInk;
        if (label == 2) return _labelPaper;
        foreach ((double tx, double ty, int l, int rgt, int _) in _tiles)
        {
            // Tile frame rotated 180°: upright when the disc is turned so the tiles are at the top.
            double u = -(x - tx), v = -(y - ty);
            const double half = 3.6, dot = .9;
            if (Math.Abs(u) > half || Math.Abs(v) > half) continue;
            // "L" for the left eye's cells, "R" for the right eye's, both centred in the tile.
            char letter = leftCell ? 'L' : 'R';
            int gx = (int)Math.Floor((u + 2.5 * dot) / dot), gy = (int)Math.Floor((v + 3.5 * dot) / dot);
            bool ink = gx >= 0 && gx < 5 && gy >= 0 && gy < 7 && LabelMask.Font[letter][gy * 6 + gx] == '1';
            return ink ? (leftCell ? l : rgt) : _random;
        }
        return -1;
    }

    /// <summary>One line for the window: which patterns, and how far apart.</summary>
    public static string Summary(StereoPairRequest r)
    {
        Grating[] views = ChooseViews(r);
        double target = TargetSeparation(r.DistanceCm);
        if (views.Length == 2)
        {
            double l = views[0].AngleDegrees(550, 133.3), rr = views[1].AngleDegrees(550, 133.3);
            string stretch = r.RightStretch == 0 ? "" : views[1].Code != ChoosePair(r).Right.Code
                ? $" (stretched +{r.RightStretch}%)" : $" (+{r.RightStretch}% needs the 66 right pattern: not applied)";
            return $"Left eye {views[0].Code} (~{l:0.0}°)  ·  right eye {views[1].Code}{stretch} (~{rr:0.0}°)  ·  {Math.Abs(l - rr):0.0}° apart; " +
                   $"your eyes are ~{target:0.0}° apart at {r.DistanceCm:0} cm";
        }
        double step = Math.Abs(views[0].AngleDegrees(550, 133.3) - views[^1].AngleDegrees(550, 133.3)) / (views.Length - 1);
        return $"{views.Length} views, left to right: {string.Join(" ", views.Select(v => v.Code))}  ·  ~{step:0.0}° between views; " +
               $"your eyes are ~{target:0.0}° apart at {r.DistanceCm:0} cm, so each eye sees about {Math.Max(1, Math.Round(target / step)):0} view(s) over from the other";
    }

    public string Describe(DvdGeometry g)
    {
        if (_patches != null) return DescribePatches(g);
        double target = TargetSeparation(DistanceCm);
        string A(Grating gr) => string.Join(" / ", new[] { 450.0, 550, 650 }.Select(w => gr.AngleDegrees(w, g.BitNm)).Select(a => double.IsNaN(a) ? "--" : $"{a:0.0}"));
        var lines = new List<string>
        {
            "HOLOGRAM DISC - KEY",
            $"Disc layout preset: {g.Name} ({g.Id}), {g.Sectors:N0} sectors",
            "",
        };
        if (_n == 2)
        {
            lines.Add($"Left eye : {Names[0],-36} pattern {PairLeft.Code,-4} angles {A(PairLeft)} deg (blue/green/red)");
            lines.Add($"Right eye: {Names[1],-36} pattern {PairRight.Code,-4} angles {A(PairRight)} deg");
            lines.Add($"Pattern separation {Math.Abs(PairLeft.AngleDegrees(550, g.BitNm) - PairRight.AngleDegrees(550, g.BitNm)):0.0} deg (green); your eyes are ~{target:0.0} deg apart at {DistanceCm:0} cm.");
            if (_stretch != 0)
                lines.Add($"Right view stretched +{_stretch}% ({PairRight.Label}): its colours shift toward red, so the left eye sees less of it.");
            else if (_stretchAsked != 0)
                lines.Add($"Right view stretch +{_stretchAsked}% asked for, but it only works with the 66 right pattern: not applied.");
        }
        else
        {
            for (int i = 0; i < _n; i++)
                lines.Add($"View {i + 1}{(i == 0 ? " (left-most)" : i == _n - 1 ? " (right-most)" : "")}".PadRight(22) + $"{Names[i],-34} pattern {Views[i].Code,-4} angles {A(Views[i])} deg");
            double step = Math.Abs(Views[0].AngleDegrees(550, g.BitNm) - Views[^1].AngleDegrees(550, g.BitNm)) / (_n - 1);
            lines.Add($"~{step:0.0} deg (green) between neighbouring views; your eyes are ~{target:0.0} deg apart at {DistanceCm:0} cm.");
            lines.Add($"Each view gets 1/{_n} of the dots, so it is dimmer than a 2-view disc.");
        }
        string moved = _offsetX == 0 && _offsetY == 0 ? "" :
            $", moved {Math.Abs(_offsetX):0.#} mm {(_offsetX < 0 ? "left" : "right")} and {Math.Abs(_offsetY):0.#} mm {(_offsetY < 0 ? "down" : "up")}";
        string copies = _copies == 1 ? "" : _uprightBottom ? ", 2 copies (top and bottom, both upright)"
            : $", {_copies} copies around the disc (each upright when turned to the top)";
        lines.Add(_offFixed
            ? $"Off areas: fixed pattern {_offGrating.Code} (~{_offGrating.AngleDegrees(550, g.BitNm):0.0} deg green, aimed away from your eyes)."
            : "Off areas: random mix (the original texture).");
        lines.Add($"Picture: {WidthMm:0.0} x {HeightMm:0.0} mm at 12 o'clock{(_large ? ", large (edges clipped by the disc)" : "")}{moved}{copies}, " +
            $"{_n} views interleaved in {_cell:0.###} mm cells.");
        lines.AddRange(
        [
            "",
            "HOW TO LOOK",
            "1. Picture at 12 o'clock (top), disc upright in front of you, about " + $"{DistanceCm:0}" + " cm away.",
            "2. One small bright light (phone flashlight, bare LED) off to one SIDE, roughly level with the disc,",
            "   not between your eyes. A dark room helps.",
            "3. Close one eye, then the other: each eye should see its own view light up in colour.",
            "   Move the light or tilt the disc slowly until both eyes see theirs, then open both eyes.",
        ]);
        if (_n > 2) lines.Add("4. Move your head slowly left and right: the picture turns as your eyes pass from view to view.");
        lines.Add((_n > 2 ? "5" : "4") + ". If the depth looks inside-out, burn again with 'Swap eyes' ticked (or move the light to the other side).");
        if (_testTiles)
        {
            lines.AddRange(
            [
                "",
                "TEST TILES (6 o'clock, printed upside down: turn the disc so they're at the top)",
                "Each tile holds an 'L' drawn for the left eye and an 'R' drawn for the right eye in the same spot.",
                "Close one eye at a time: the tile where your left eye sees only L and your right eye only R is",
                "the pattern pair that works best for your discs and light. With 2 views, use it via 'Pattern pair' and burn again.",
                "",
                "Tile  Left eye  Right eye  Separation (green)",
            ]);
            for (int i = 0; i < TestPairs.Length; i++)
            {
                Grating a = Grating.FromCode(TestPairs[i].Left), b = Grating.FromCode(TestPairs[i].Right);
                lines.Add($"{i + 1,4}  {a.Code,-8}  {b.Code,-9}  {Math.Abs(a.AngleDegrees(550, g.BitNm) - b.AngleDegrees(550, g.BitNm)):0.0} deg");
            }
        }
        return string.Join(Environment.NewLine, lines);
    }

    private string DescribePatches(DvdGeometry g)
    {
        double target = TargetSeparation(DistanceCm);
        string A(Grating gr) => string.Join(" / ", new[] { 450.0, 550, 650 }.Select(w => gr.AngleDegrees(w, g.BitNm)).Select(a => double.IsNaN(a) ? "--" : $"{a:0.0}"));
        var lines = new List<string>
        {
            "HOLOGRAM TEST PATCHES - KEY",
            $"Disc layout preset: {g.Name} ({g.Id}), {g.Sectors:N0} sectors",
            "",
            $"Left eye pattern  {PairLeft.Code,-6} angles {A(PairLeft)} deg (blue/green/red)",
            $"Right eye pattern {PairRight.Code,-6} angles {A(PairRight)} deg",
            $"Your eyes are ~{target:0.0} deg apart at {DistanceCm:0} cm. Cells: {_cell:0.###} mm checkerboard.",
            "",
            "TWELVE 6 mm PATCHES AT 12 O'CLOCK, THREE ROWS (numbered on the disc)",
            "Every row is the same four patches, left to right:  all off / left only / right only / both on.",
            "The rows differ only in the right eye's pattern (Ashley-QwQ's test): a longer period moves its",
            "colours toward red, so at the left eye it should slip past red into infrared and vanish.",
            "Off areas everywhere: random mix.",
        };
        for (int row = 0; row < _patchRight.Length; row++)
        {
            Grating right = Gratings[_patchRight[row]];
            int first = row * 4 + 1;
            string where = row == 0 ? "outer" : row == _patchRight.Length - 1 ? "inner" : "middle";
            string name = row == 0 ? "+0 %  (as now)" : _patchRight[row] == 1 ? $"+{Grating.Stretches[row]} %  (not available for {PairRight.Code}: same as row 1)" : $"+{Grating.Stretches[row]} %";
            lines.Add($"  {where,-6} row  {$"{first}-{first + 3}",-5}  right pattern {right.Code,-6} angles {A(right)} deg   {name}");
        }
        lines.AddRange(
        [
            "",
            "HOW TO TEST",
            "1. Fix the disc upright (patches at the top) and the lamp in place. Don't move either, and keep",
            "   the lamp in exactly the same place for every photo, on every disc, so the shots compare.",
            "2. Put the camera (or your eye) where your LEFT eye would be. Lock the exposure. Photograph.",
            "3. Move it sideways by your eye spacing (~6.5 cm) to the RIGHT eye position. Photograph again.",
            "4. In each row, what a working setup looks like:",
            "   - Left position:  'left only' and 'both' bright; 'right only' about as dark as 'all off'.",
            "   - Right position: 'right only' and 'both' bright; 'left only' about as dark as 'all off'.",
            "5. Pick the row where 'right only' is darkest at the LEFT position while it still shows well at",
            "   the RIGHT position (it turns from green toward yellow there). Use that stretch for your holograms",
            "   ('Right view pattern' in the app, or --stretch 10 / --stretch 14).",
            "6. Please share the two photos and this key on the HoloStudio3D or disc-probe GitHub page.",
        ]);
        return Finish(lines);
    }

    private string Finish(List<string> lines)
    {
        if (_tiles.Count > 0)
            lines.AddRange(["", "The L/R test tiles at 6 o'clock are on this disc too (see the hologram key for how to read them)."]);
        return string.Join(Environment.NewLine, lines);
    }

    // ── picture loading ──

    /// <summary>Saves all the views side by side (left-most view on the left) so they can be checked.</summary>
    public void SaveViews(string path)
    {
        int w = _previewW, h = _previewH, total = _n * w;
        byte[] pixels = new byte[total * h];
        for (int v = 0; v < _n; v++)
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            pixels[y * total + v * w + x] = (byte)Math.Round(Math.Clamp(_previews[v][y * w + x], 0, 1) * 255);
        RasterCodec.SavePng(path, total, h, pixels, grayscale: true);
    }

    /// <summary>
    /// Works out the picture's shape and the views' names, and returns a function that builds view i
    /// (a grid with its size in the last two entries) on demand.
    /// </summary>
    private static (double Aspect, string[] Names, Func<int, double[]> View) PlanViews(StereoPairRequest r, double[] positions)
    {
        int n = positions.Length;
        string Side(int i) => n == 2 ? (i == 0 ? "left view" : "right view") : $"view {i + 1}";
        if (r.Mode == "depth")
        {
            BgraRaster color = Load(r.ColorFile), depthRaster = Load(r.DepthFile);
            (double[] img, int w, int h) = Shrink(Luminance(color, 0, color.Width), ViewCells);
            double[] depth = Resize(Luminance(depthRaster, 0, depthRaster.Width), w, h, r.InvertDepth);
            double aspect = (double)w / h;
            double strength = Math.Clamp(r.DepthStrength, 0, 12), focus = Math.Clamp(r.DepthFocus, 0, 1);
            string picture = Path.GetFileName(r.ColorFile);
            return (aspect, Enumerable.Range(0, n).Select(i => $"{picture} ({Side(i)})").ToArray(),
                i => Fit(SynthesizeView(img, depth, w, h, strength, focus, positions[i]), w, h, ViewCells, aspect));
        }
        if (r.Mode == "model")
        {
            var model = new ModelRender(r.ModelFile);
            string name = Path.GetFileName(r.ModelFile);
            double turn = Math.Clamp(r.ModelTurn, 0, 45);
            return (1, Enumerable.Range(0, n).Select(i => $"{name} ({Side(i)})").ToArray(),
                // A view from the left shows more of the model's left side: it is turned the other way.
                i => model.Render(1800, r.ModelYaw - positions[i] * turn, r.ModelPitch, r.ModelZoom));
        }
        if (r.Mode == "frames")
        {
            string[] files = r.FrameFiles;
            BgraRaster first = Load(files[0]);
            double aspect = (double)first.Width / first.Height;
            return (aspect, files.Select(Path.GetFileName).ToArray(), i =>
            {
                BgraRaster raster = i == 0 ? first : Load(files[i]);
                (double[] v, int w, int h) = Luminance(raster, 0, raster.Width);
                return Fit(v, w, h, ViewCells, aspect);
            });
        }
        if (r.Mode == "two")
        {
            BgraRaster left = Load(r.LeftFile), right = Load(r.RightFile);
            (double[] l, int lw, int lh) = Luminance(left, 0, left.Width);
            (double[] rr, int rw, int rh) = Luminance(right, 0, right.Width);
            double aspect = (double)lw / lh;
            double[][] views = [Fit(l, lw, lh, ViewCells, aspect), Fit(rr, rw, rh, ViewCells, aspect)];
            return (aspect, [Path.GetFileName(r.LeftFile), Path.GetFileName(r.RightFile)], i => views[i]);
        }
        BgraRaster sbs = Load(r.SbsFile);
        if (sbs.Width < 4) throw new ArgumentException("The side-by-side picture is too small.");
        int half = sbs.Width / 2;
        (double[] a, int aw, int ah) = Luminance(sbs, 0, half);
        (double[] b, int bw, int bh) = Luminance(sbs, half, half);
        // A squeezed (half-width) side-by-side image is stretched back to its full width.
        double viewAspect = (double)aw / ah * (r.Squeezed ? 2 : 1);
        bool crossed = r.CrossEyed; // cross-eyed layout puts the right-eye view on the left
        string sbsName = Path.GetFileName(r.SbsFile);
        double[][] halves = [Fit(crossed ? b : a, crossed ? bw : aw, ah, ViewCells, viewAspect), Fit(crossed ? a : b, crossed ? aw : bw, bh, ViewCells, viewAspect)];
        return (viewAspect, [sbsName + (crossed ? " (right half)" : " (left half)"), sbsName + (crossed ? " (left half)" : " (right half)")], i => halves[i]);
    }

    /// <summary>Left and right views from a picture and its depth map (see <see cref="SynthesizeView"/>).</summary>
    internal static (double[] Left, double[] Right) Synthesize(double[] image, double[] depth, int w, int h, double strengthPercent, double focus) =>
        (SynthesizeView(image, depth, w, h, strengthPercent, focus, -.5), SynthesizeView(image, depth, w, h, strengthPercent, focus, .5));

    /// <summary>
    /// Builds one view from a picture and its depth map (depth-image-based rendering): each pixel
    /// shifts sideways by its depth, nearer pixels win, and the gaps they uncover are filled from
    /// the background side. position is where the view is seen from, in eye-separations
    /// (-0.5 = left eye, +0.5 = right eye); "3D strength" is the shift between the two eyes.
    /// </summary>
    internal static double[] SynthesizeView(double[] image, double[] depth, int w, int h, double strengthPercent, double focus, double position)
    {
        double maxShift = strengthPercent / 100.0 * w;
        double sign = -2 * position;
        {
            double[] output = new double[w * h];
            float[] zbuffer = new float[w * h];
            Array.Fill(zbuffer, -1f);
            Parallel.For(0, h, y =>
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    float z = (float)depth[row + x];
                    double shift = (z - focus) * maxShift / 2;
                    // Spread each source pixel over the span it covers, so stretched areas don't crack.
                    double a = x + sign * shift, b = x + 1 + sign * shift;
                    int x0 = (int)Math.Floor(a), x1 = Math.Max(x0, (int)Math.Ceiling(b) - 1);
                    for (int xd = Math.Max(0, x0); xd <= Math.Min(w - 1, x1); xd++)
                    {
                        if (z <= zbuffer[row + xd]) continue;
                        zbuffer[row + xd] = z;
                        output[row + xd] = image[row + x];
                    }
                }
                // Fill uncovered gaps from whichever neighbour is farther away (the background).
                for (int x = 0; x < w; x++)
                {
                    if (zbuffer[row + x] >= 0) continue;
                    int l = x - 1; while (l >= 0 && zbuffer[row + l] < 0) l--;
                    int r = x + 1; while (r < w && zbuffer[row + r] < 0) r++;
                    int source = l < 0 ? r : r >= w ? l : zbuffer[row + l] <= zbuffer[row + r] ? l : r;
                    if (source >= 0 && source < w) output[row + x] = output[row + source];
                }
            });
            return output;
        }
    }

    private static (double[] Values, int Width, int Height) Shrink((double[] Values, int Width, int Height) source, int maxWidth)
    {
        (double[] v, int w, int h) = source;
        if (w <= maxWidth) return source;
        int nw = maxWidth, nh = Math.Max(1, (int)Math.Round((double)h * maxWidth / w));
        double[] grid = new double[w * h + 2];
        Array.Copy(v, grid, v.Length);
        grid[^2] = w; grid[^1] = h;
        return (Resample(grid, nw, nh), nw, nh);
    }

    private static double[] Resize((double[] Values, int Width, int Height) source, int w, int h, bool invert)
    {
        (double[] v, int sw, int sh) = source;
        double[] result = new double[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            // Bilinear, so depth steps stay smooth when the depth map is smaller than the picture.
            double fx = Math.Clamp((x + .5) * sw / w - .5, 0, sw - 1), fy = Math.Clamp((y + .5) * sh / h - .5, 0, sh - 1);
            int x0 = (int)fx, y0 = (int)fy, x1 = Math.Min(x0 + 1, sw - 1), y1 = Math.Min(y0 + 1, sh - 1);
            double tx = fx - x0, ty = fy - y0;
            double d = (v[y0 * sw + x0] * (1 - tx) + v[y0 * sw + x1] * tx) * (1 - ty) + (v[y1 * sw + x0] * (1 - tx) + v[y1 * sw + x1] * tx) * ty;
            result[y * w + x] = invert ? 1 - d : d;
        }
        return result;
    }

    private static BgraRaster Load(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) throw new ArgumentException("Choose the picture(s) first.");
        return RasterCodec.LoadBgra32(path, 512L * 1024 * 1024, 200_000_000);
    }

    private static (double[] Values, int Width, int Height) Luminance(BgraRaster raster, int x0, int width)
    {
        double[] v = new double[width * raster.Height];
        for (int y = 0; y < raster.Height; y++)
        for (int x = 0; x < width; x++)
        {
            int o = (y * raster.Width + x0 + x) * 4;
            double lum = (.0722 * raster.Pixels[o] + .7152 * raster.Pixels[o + 1] + .2126 * raster.Pixels[o + 2]) / 255.0;
            v[y * width + x] = lum * raster.Pixels[o + 3] / 255.0;
        }
        return (v, width, raster.Height);
    }

    /// <summary>Stretches a view to an exact grid of the target aspect (keeps both eyes aligned).</summary>
    private static double[] Fit(double[] source, int sw, int sh, int widthCells, double aspect)
    {
        int w = widthCells, h = Math.Max(1, (int)Math.Round(widthCells / aspect));
        double[] result = new double[w * h + 2];
        result[^2] = w; result[^1] = h; // remember the grid size in the tail
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int sx = Math.Min(sw - 1, (int)((x + .5) * sw / w)), sy = Math.Min(sh - 1, (int)((y + .5) * sh / h));
            result[y * w + x] = source[sy * sw + sx];
        }
        return result;
    }

    private static double[] Resample(double[] grid, int nx, int ny)
    {
        int w = (int)grid[^2], h = (int)grid[^1];
        double[] result = new double[nx * ny];
        for (int y = 0; y < ny; y++)
        for (int x = 0; x < nx; x++)
        {
            // Box-average the source cells under this output cell.
            int x0 = x * w / nx, x1 = Math.Max(x0 + 1, (x + 1) * w / nx), y0 = y * h / ny, y1 = Math.Max(y0 + 1, (y + 1) * h / ny);
            double sum = 0; int n = 0;
            for (int yy = y0; yy < Math.Min(h, y1); yy++)
            for (int xx = x0; xx < Math.Min(w, x1); xx++) { sum += grid[yy * w + xx]; n++; }
            result[y * nx + x] = n > 0 ? sum / n : 0;
        }
        return result;
    }

    /// <summary>
    /// Floyd–Steinberg error diffusion in serpentine order: grays become an even, fine-grained
    /// spread of cells instead of noise. A hard cut-off is used when dithering is off.
    /// </summary>
    /// <summary>
    /// Dithers one view using only the cells that belong to it, so each view keeps its own gray
    /// level. (Dithering the whole grid first and then picking a view's cells lets the dot pattern
    /// line up with the interleave: a 50% gray could come out fully on for one eye and off for the
    /// other.) A view's cells in row y are the columns x with (x + stride * y) mod n == view; they
    /// are gathered into a compact grid, dithered there with Floyd-Steinberg, and put back.
    /// </summary>
    internal static bool[] DitherView(double[] value, int width, int n, int stride, int view, bool dither, double threshold)
    {
        int height = value.Length / width;
        int compact = (width + n - 1) / n;
        double[] packed = new double[compact * height];
        int[] column = new int[compact * height];
        for (int y = 0; y < height; y++)
        {
            int offset = ((view - stride * y) % n + n) % n;
            for (int k = 0; k < compact; k++)
            {
                int x = offset + k * n;
                int j = y * compact + k;
                if (x < width) { packed[j] = value[y * width + x]; column[j] = x; }
                else { packed[j] = value[y * width + width - 1]; column[j] = -1; } // padding, not written back
            }
        }
        bool[] packedOn = Dither(packed, compact, dither, threshold);
        bool[] on = new bool[value.Length];
        for (int j = 0; j < packed.Length; j++)
            if (column[j] >= 0) on[(j / compact) * width + column[j]] = packedOn[j];
        return on;
    }

    private static bool[] Dither(double[] value, int width, bool dither, double threshold)
    {
        bool[] on = new bool[value.Length];
        if (!dither)
        {
            for (int i = 0; i < on.Length; i++) on[i] = value[i] >= threshold;
            return on;
        }
        int height = value.Length / width;
        double[] v = (double[])value.Clone();
        for (int y = 0; y < height; y++)
        {
            bool reverse = (y & 1) == 1;
            int step = reverse ? -1 : 1;
            for (int k = 0; k < width; k++)
            {
                int x = reverse ? width - 1 - k : k;
                int i = y * width + x;
                bool lit = v[i] >= .5;
                on[i] = lit;
                double error = v[i] - (lit ? 1 : 0);
                int ahead = x + step, behind = x - step;
                if (ahead >= 0 && ahead < width) v[i + step] += error * 7 / 16;
                if (y + 1 < height)
                {
                    int below = i + width;
                    if (behind >= 0 && behind < width) v[below - step] += error * 3 / 16;
                    v[below] += error * 5 / 16;
                    if (ahead >= 0 && ahead < width) v[below + step] += error * 1 / 16;
                }
            }
        }
        return on;
    }

    private static double Hash(int i)
    {
        uint h = (uint)i * 2654435761u;
        h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
        return (h & 0xFFFF) / 65535.0;
    }
}
