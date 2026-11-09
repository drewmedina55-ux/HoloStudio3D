using System.Globalization;

namespace Disc3DLab;

internal static class Program
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    [STAThread]
    public static int Main(string[] args)
    {
        // The program is a windowed app; when run from a terminal with a command, borrow that terminal.
        if (args.Length > 0 && OperatingSystem.IsWindows()) AttachConsole(-1);
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch (IOException) { }
        try
        {
            if (args.Length == 0) return LabWindow.Run(new LabEngine());
            if (args[0] is "--help" or "-h" or "help") { PrintHelp(); return 0; }
            Options options = Options.Parse(args.Skip(1));
            switch (args[0])
            {
                case "stereogram": RunStereogram(options); break;
                case "diffraction-test": RunTestDisc(options); break;
                case "tilt-image": RunTiltImage(options); break;
                case "hologram":
                case "stereo-pair": RunStereoPair(options); break;
                case "presets":
                    foreach (DvdGeometry g in DvdGeometry.BuiltIn()) Console.WriteLine($"{g.Id,-32} {g.Name}");
                    break;
                default: throw new ArgumentException($"Unknown command '{args[0]}'. Run with --help.");
            }
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidDataException or FormatException)
        {
            Console.Error.WriteLine("Error: " + exception.Message);
            return 1;
        }
    }

    private static void PrintHelp() => Console.WriteLine("""
        HoloStudio3D - hidden-3D stereograms and diffraction (tilt) images for Disc Image Studio.
        Run with no arguments (or double-click) to open the HoloStudio3D window.

        stereogram        Make a black/white "Magic Eye" image to load into Disc Image Studio.
          --demo NAME       torus | rings | spheres | ripple | star
          --depth FILE      depth map picture (white = near, black = far); add --invert for the opposite
          --mesh FILE       .obj or .stl model  [--yaw DEG] [--pitch DEG] [--diameter MM (default 70)]
          --out FILE        output PNG (default stereogram.png)
          --separation MM   pattern repeat width, default 14 (smaller = easier to see, less depth)
          --depth-factor F  how far it pops out, 0.1-0.5, default 0.33
          --dot MM          dot size, default 0.15 (use 0.25+ for CD-R)
          --size PX         output size for the 120 mm disc, default 2400
          --seed N          random dot pattern seed
          --no-guides       leave out the two guide dots

        diffraction-test  Make a DVD test disc image (.iso) with 12 rings of grating textures.
          --out FILE        output .iso (default diffraction-test.iso)
          --preset ID       DVD layout from the app's presets (default dvd-5-120mm; see 'presets')
          --threads N       CPU threads (default: all)

        tilt-image        Make a DVD image (.iso) where up to 4 pictures each flash at their own tilt.
          --image1..--image4 FILE   pictures (bright = drawn); image1 shows at the smallest tilt
          --gratings LIST   override textures, e.g. 92,B9,66,A5 (from the test disc)
          --threshold F     0-1 cut-off instead of dithering grays
          --out, --preset, --threads   as above

        hologram          Make a Hologram mode DVD image (.iso): a 3D picture from 2-7 views (experimental).
                          ("stereo-pair" still works as the old name.)
          --image FILE --depthmap FILE   picture + depth map (white = near)
          --model FILE      a 3D model (.obj or .stl)
          --frames A,B,C    2-7 pictures, left-most view first (e.g. lenticular frames)
          --sbs FILE        side-by-side picture (left view on the left), or --left FILE --right FILE
          --views N         picture + depth or model: how many views, 2-7 (default 2)
          --turn DEG        model: how far it turns between your two eyes (default 6)
          --yaw DEG, --pitch DEG, --zoom F   model: starting angle and size
          --strength PCT    picture + depth: biggest shift, % of width (default 3)
          --focus F         picture + depth: 0-1, this depth sits on the disc surface (default 0.5)
          --invert-depth    the depth map has black = near
          --squeezed        the side-by-side picture is half-width (each half squeezed)
          --cross-eyed      the side-by-side picture has the right view on the left
          --distance CM     viewing distance, default 40 (picks the pattern pair)
          --pair L,R        force the patterns, e.g. B9,92
          --swap            swap which eye gets which pattern
          --width MM        picture width at 12 o'clock, default 50 (up to 80, or 116 with --large)
          --large           let the picture fill more of the disc; the disc edges and hole clip it
          --move-x MM       slide the picture right (+) or left (-), up to 40 mm
          --move-y MM       slide the picture up toward the rim (+) or down (-), up to 30 mm
          --copies N        repeat the picture around the disc, 1-8 (each upright when turned to the top)
          --upright-bottom  with --copies 2: keep the bottom copy upright, so both show at once
          --detail MM       dither cell size: 0.05 fine (default), 0.1 normal, 0.2 coarse
          --no-test         leave out the L/R test tiles at 6 o'clock
          --out, --preset, --threads   as above

        presets           List the DVD layout presets.
        """);

    // ───────────────────────── commands ─────────────────────────

    private static void RunStereogram(Options o)
    {
        int size = o.Int("--size", 2400);
        if (size is < 600 or > 8000) throw new ArgumentException("--size must be 600-8000.");
        DepthMap depth;
        string source;
        if (o.Has("--mesh"))
        {
            source = o.Path("--mesh");
            depth = DepthMap.FromMesh(source, size, o.Double("--yaw", 0), o.Double("--pitch", 0), o.Double("--diameter", 70));
        }
        else if (o.Has("--depth"))
        {
            source = o.Path("--depth");
            depth = DepthMap.FromImage(source, size, o.Has("--invert"));
        }
        else
        {
            source = o.Text("--demo", "torus");
            depth = DepthMap.Demo(source, size);
        }

        StereogramOptions settings = new(
            FarSeparationMm: o.Double("--separation", 14),
            DepthFactor: o.Double("--depth-factor", 1.0 / 3),
            DotMm: o.Double("--dot", .15),
            Seed: o.Int("--seed", 1),
            GuideDots: !o.Has("--no-guides"));
        if (settings.FarSeparationMm is < 5 or > 40) throw new ArgumentException("--separation must be 5-40 mm.");
        if (settings.DepthFactor is < .05 or > .6) throw new ArgumentException("--depth-factor must be 0.05-0.6.");
        if (settings.DotMm is < .02 or > 2) throw new ArgumentException("--dot must be 0.02-2 mm.");

        string output = o.OutPath("stereogram.png");
        Console.WriteLine($"Rendering hidden-3D image from {Path.GetFileName(source)}...");
        byte[] image = Stereogram.Render(depth, settings);
        Stereogram.Save(output, image, size);
        string depthPreview = Path.ChangeExtension(output, null) + "-depth.png";
        if (!File.Exists(depthPreview) || o.Has("--overwrite")) depth.SavePreview(depthPreview);
        Console.WriteLine($"Saved {output}");
        Console.WriteLine($"Depth preview: {depthPreview}");
        Console.WriteLine("""

            Next: in Disc Image Studio, start a project, add this PNG as the image layer, fill the
            disc (120 mm), and use 2 tone levels. To view the burned disc: hold it 30-40 cm away,
            look 'through' it as if at something far behind, and let the two guide dots at the top
            become three. The shape then floats in depth.
            """);
    }

    private static void RunTestDisc(Options o)
    {
        DvdGeometry geometry = DvdGeometry.Find(o.Text("--preset", "dvd-5-120mm"));
        string output = o.OutPath("diffraction-test.iso");
        TestDiscLayout layout = new();
        WriteKeyAndMap(layout, geometry, output);
        Console.WriteLine($"Generating the full DVD image ({geometry.Sectors * 2048L / 1e9:0.0} GB). This takes a while...");
        WriteWithCtrlC(layout, geometry, output, o);
        PrintBurnHint(output);
    }

    private static void RunTiltImage(Options o)
    {
        List<string> images = [];
        for (int k = 1; k <= 4; k++) if (o.Has($"--image{k}")) images.Add(o.Path($"--image{k}"));
        if (images.Count == 0) throw new ArgumentException("Give at least --image1.");
        IReadOnlyList<Grating> layers = o.Has("--gratings")
            ? o.Text("--gratings", "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Grating.FromCode).ToList()
            : Grating.TiltLayers;
        bool dither = !o.Has("--threshold");
        TiltImageLayout layout = new(images, layers, o.Double("--threshold", .5), dither);
        DvdGeometry geometry = DvdGeometry.Find(o.Text("--preset", "dvd-5-120mm"));
        string output = o.OutPath("tilt-image.iso");
        WriteKeyAndMap(layout, geometry, output);
        Console.WriteLine($"Generating the full DVD image ({geometry.Sectors * 2048L / 1e9:0.0} GB). This takes a while...");
        WriteWithCtrlC(layout, geometry, output, o);
        PrintBurnHint(output);
    }

    private static void RunStereoPair(Options o)
    {
        StereoPairRequest request = new()
        {
            Mode = o.Has("--sbs") ? "sbs" : o.Has("--image") ? "depth" : o.Has("--model") ? "model" : o.Has("--frames") ? "frames" : "two",
            ViewCount = o.Int("--views", 2),
            ModelFile = o.Has("--model") ? o.Path("--model") : null,
            ModelTurn = o.Double("--turn", 6),
            ModelYaw = o.Double("--yaw", 0),
            ModelPitch = o.Double("--pitch", 0),
            ModelZoom = o.Double("--zoom", 1),
            FrameFiles = o.Has("--frames") ? o.Text("--frames", "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(System.IO.Path.GetFullPath).ToArray() : null,
            ColorFile = o.Has("--image") ? o.Path("--image") : null,
            DepthFile = o.Has("--depthmap") ? o.Path("--depthmap") : null,
            InvertDepth = o.Has("--invert-depth"),
            DepthStrength = o.Double("--strength", 3),
            DepthFocus = o.Double("--focus", .5),
            SbsFile = o.Has("--sbs") ? o.Path("--sbs") : null,
            LeftFile = o.Has("--left") ? o.Path("--left") : null,
            RightFile = o.Has("--right") ? o.Path("--right") : null,
            Squeezed = o.Has("--squeezed"),
            CrossEyed = o.Has("--cross-eyed"),
            DistanceCm = o.Double("--distance", 40),
            SwapEyes = o.Has("--swap"),
            WidthMm = o.Double("--width", 50),
            Large = o.Has("--large"),
            OffsetXMm = o.Double("--move-x", 0),
            OffsetYMm = o.Double("--move-y", 0),
            Copies = o.Int("--copies", 1),
            UprightBottom = o.Has("--upright-bottom"),
            CellMm = o.Double("--detail", .05),
            TestStrips = !o.Has("--no-test"),
            Preset = o.Text("--preset", "dvd-5-120mm"),
        };
        if (o.Has("--pair"))
        {
            string[] pair = o.Text("--pair", "").Split(',', StringSplitOptions.TrimEntries);
            if (pair.Length != 2) throw new ArgumentException("--pair needs two patterns, e.g. B9,92 (left,right).");
            request.PairLeft = pair[0]; request.PairRight = pair[1];
        }
        if (request.Mode == "two" && (request.LeftFile is null || request.RightFile is null))
            throw new ArgumentException("Give --image FILE --depthmap FILE, --model FILE, --frames A,B,C, --sbs FILE, or both --left FILE and --right FILE.");
        if (request.Mode == "depth" && request.DepthFile is null) throw new ArgumentException("--image needs --depthmap FILE too.");
        StereoPairLayout layout = new(request);
        DvdGeometry geometry = DvdGeometry.Find(request.Preset);
        string output = o.OutPath("hologram.iso");
        WriteKeyAndMap(layout, geometry, output);
        layout.SaveViews(Path.ChangeExtension(output, null) + "-views.png");
        Console.WriteLine($"Views (left-most first): {Path.ChangeExtension(output, null)}-views.png");
        Console.WriteLine($"Generating the full DVD image ({geometry.Sectors * 2048L / 1e9:0.0} GB). This takes a while...");
        WriteWithCtrlC(layout, geometry, output, o);
        PrintBurnHint(output);
    }

    private static void WriteWithCtrlC(IDiffractionLayout layout, DvdGeometry geometry, string output, Options o)
    {
        using CancellationTokenSource cancel = new();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancel.Cancel(); };
        Console.CancelKeyPress += handler;
        try
        {
            DiscWriter.Write(layout, geometry, output, o.Int("--threads", 0),
                o.Has("--sectors") ? (uint)o.Int("--sectors", 0) : null, null, cancel.Token);
        }
        finally { Console.CancelKeyPress -= handler; }
    }

    private static void WriteKeyAndMap(IDiffractionLayout layout, DvdGeometry geometry, string output)
    {
        string stem = Path.ChangeExtension(output, null);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(stem + "-key.txt", layout.Describe(geometry));
        DiscWriter.SaveMap(layout, geometry, stem + "-map.png");
        Console.WriteLine($"Key: {stem}-key.txt");
        Console.WriteLine($"Layout map (false colour): {stem}-map.png");
    }

    private static void PrintBurnHint(string output) => Console.WriteLine($"""

        Burn {Path.GetFileName(output)} to a blank single-layer DVD-R/+R as a disc IMAGE:
          Windows: right-click the .iso > "Burn disc image", or ImgBurn > "Write image file to disc".
        Don't add it to a data disc as a file. It has no file system on purpose, so it won't open
        in Explorer. Then read the -key.txt for how to look at it.
        """);
}

/// <summary>--name value pairs; bare flags (--invert, --no-guides, --overwrite) take no value.</summary>
internal sealed class Options
{
    private static readonly HashSet<string> Flags = ["--invert", "--no-guides", "--overwrite", "--squeezed", "--cross-eyed", "--swap", "--no-test", "--invert-depth", "--large"];
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public static Options Parse(IEnumerable<string> arguments)
    {
        Options options = new();
        string[] args = arguments.ToArray();
        for (int i = 0; i < args.Length; i++)
        {
            string name = args[i];
            if (!name.StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Unexpected '{name}'.");
            if (Flags.Contains(name)) { options._values[name] = "true"; continue; }
            if (i + 1 >= args.Length) throw new ArgumentException($"{name} needs a value.");
            if (!options._values.TryAdd(name, args[++i])) throw new ArgumentException($"{name} given twice.");
        }
        return options;
    }

    public bool Has(string name) => _values.ContainsKey(name);
    public string Text(string name, string fallback) => _values.TryGetValue(name, out string? v) ? v : fallback;

    public int Int(string name, int fallback) => _values.TryGetValue(name, out string? v)
        ? int.Parse(v, CultureInfo.InvariantCulture) : fallback;

    public double Double(string name, double fallback) => _values.TryGetValue(name, out string? v)
        ? double.Parse(v, NumberStyles.Float, CultureInfo.InvariantCulture) : fallback;

    public string Path(string name)
    {
        string path = System.IO.Path.GetFullPath(Text(name, ""));
        if (!File.Exists(path)) throw new ArgumentException($"File not found: {path}");
        return path;
    }

    public string OutPath(string fallback) => System.IO.Path.GetFullPath(Text("--out", fallback));
}
