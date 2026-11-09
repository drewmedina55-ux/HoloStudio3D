#nullable disable
using System.Diagnostics;

namespace Disc3DLab;

/// <summary>The real engine behind the window: stereograms, layouts and disc images.</summary>
internal sealed class LabEngine : ILabBackend
{
    private IList<PresetInfo> _presets;

    public IList<PresetInfo> Presets => _presets ??= DvdGeometry.BuiltIn()
        .Select(g => new PresetInfo { Id = g.Id, Name = g.Name }).ToList();

    public void ReloadPresets()
    {
        DvdGeometry.Reload();
        _presets = null;
    }

    // ── Stage 1: disc-probe ──

    /// <summary>The bundled copy: disc-probe\python\python.exe and disc-probe\src next to the program.
    /// DISC3DLAB_PYTHON / DISC3DLAB_PROBE_SRC override them (used for testing on other systems).</summary>
    private static string ProbeRoot => Path.Combine(AppContext.BaseDirectory, "disc-probe");

    private static string ProbePython => Environment.GetEnvironmentVariable("DISC3DLAB_PYTHON") is { Length: > 0 } p
        ? p : Path.Combine(ProbeRoot, "python", OperatingSystem.IsWindows() ? "python.exe" : "bin/python3");

    private static string ProbeSource => Environment.GetEnvironmentVariable("DISC3DLAB_PROBE_SRC") is { Length: > 0 } s
        ? s : Path.Combine(ProbeRoot, "src");

    public string ProbeProblem
    {
        get
        {
            if (!File.Exists(Path.Combine(ProbeSource, "labrun.py")) || !Directory.Exists(Path.Combine(ProbeSource, "discprobe")))
                return "disc-probe isn't in this build (the disc-probe folder next to HoloStudio3D.exe is missing). Run Build-English.bat again.";
            if (!File.Exists(ProbePython))
                return "disc-probe's private Python wasn't set up. Run Build-English.bat again while connected to the internet.";
            return null;
        }
    }

    public string ProbeWorkFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Disc3DLab", "disc-probe");

    public IList<string> OpticalDrives() => DriveInfo.GetDrives()
        .Where(d => d.DriveType == DriveType.CDRom)
        .Select(d => d.Name.TrimEnd('\\', '/'))
        .ToList();

    public int RunProbe(IList<string> args, Action<string> line, Action<string> status, CancellationToken cancel)
    {
        string problem = ProbeProblem;
        if (problem != null) throw new InvalidOperationException(problem);
        Directory.CreateDirectory(ProbeWorkFolder);
        var start = new ProcessStartInfo(ProbePython)
        {
            WorkingDirectory = ProbeWorkFolder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        start.ArgumentList.Add("-u");
        start.ArgumentList.Add(Path.Combine(ProbeSource, "labrun.py"));
        foreach (string a in args) start.ArgumentList.Add(a);
        start.Environment["PYTHONUNBUFFERED"] = "1";
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        start.Environment["PYTHONPATH"] = ProbeSource; // the bundled Python reads its path file instead; this is for other Pythons

        using Process process = Process.Start(start) ?? throw new IOException("Couldn't start disc-probe.");
        process.StandardInput.Close(); // nothing to type: disc-probe never waits for input this way
        using CancellationTokenRegistration stop = cancel.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        });
        Task errors = Task.Run(() => Pump(process.StandardError, line, status));
        Pump(process.StandardOutput, line, status);
        errors.Wait();
        process.WaitForExit();
        cancel.ThrowIfCancellationRequested();
        return process.ExitCode;
    }

    /// <summary>Splits output into lines; text ended by a carriage return is a progress line that redraws itself.</summary>
    private static void Pump(StreamReader reader, Action<string> line, Action<string> status)
    {
        var buffer = new System.Text.StringBuilder();
        int c;
        while ((c = reader.Read()) >= 0)
        {
            if (c == '\n')
            {
                line(buffer.ToString().TrimEnd('\r'));
                buffer.Clear();
            }
            else if (c == '\r')
            {
                if (reader.Peek() == '\n') continue;
                if (buffer.Length > 0) status(buffer.ToString().Trim());
                buffer.Clear();
            }
            else buffer.Append((char)c);
        }
        if (buffer.Length > 0) line(buffer.ToString());
    }

    public string PresetNote
    {
        get
        {
            int yours = Presets.Count(p => p.Id.StartsWith("user:", StringComparison.Ordinal));
            if (DvdGeometry.UserPresetProblem != null) return DvdGeometry.UserPresetProblem;
            return yours > 0
                ? $"{yours} of your own preset(s) from Disc Image Studio are listed first (\"Yours:\")."
                : "Presets you save in Disc Image Studio, or install with disc-probe, appear here as \"Yours:\" after restarting HoloStudio3D.";
        }
    }

    public IList<GratingInfo> Gratings { get; } = Grating.TestRings.Where(g => g != Grating.Random)
        .Select(g => new GratingInfo { Code = g.Code, Angle = Math.Round(g.AngleDegrees(550, 133.3), 1) }).ToList();

    public IList<string> TiltDefaults { get; } = Grating.TiltLayers.Select(g => g.Code).ToList();
    public IList<string> Demos { get; } = DepthMap.DemoNames;
    public IList<string> StereoTestPairs { get; } = StereoPairLayout.TestPairs.Select(p => p.Left + "," + p.Right).ToList();

    /// <summary>
    /// Default save folder: the top of the system drive. Windows doesn't let normal programs write
    /// files straight into C:\ without admin rights, so it's a folder there: C:\HoloStudio3D (or a folder from an earlier name, such as C:\Disc 3D Lab, if one is already there).
    /// </summary>
    public string DefaultOutputFolder
    {
        get
        {
            string root = OperatingSystem.IsWindows()
                ? Path.GetPathRoot(Environment.SystemDirectory) is { Length: > 0 } systemRoot ? systemRoot : @"C:\"
                : Environment.CurrentDirectory;
            // Keep using a folder from before the renames if one already exists.
            foreach (string old in new[] { "DiscImageStudio Holographic", "3-Disc Lab", "Disc 3D Lab" })
                if (Directory.Exists(Path.Combine(root, old))) return Path.Combine(root, old);
            return Path.Combine(root, "HoloStudio3D");
        }
    }

    public string SettingsFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Disc3DLab");

    public void RenderStereoPreview(StereoRequest request, int size, out LabImage stereogram, out LabImage depth)
    {
        DepthMap map = Depth(request, size);
        stereogram = Gray(Stereogram.Render(map, Options(request)), size);
        byte[] pixels = new byte[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)Math.Round(Math.Clamp(map.Z[i], 0, 1) * 255);
        depth = Gray(pixels, size);
    }

    public string SaveStereogram(StereoRequest request, string folder, string name)
    {
        const int size = 2400; // 20 px per mm across the 120 mm disc
        string output = UniquePath(Path.Combine(Folder(folder), SafeName(name, "stereogram") + ".png"));
        DepthMap map = Depth(request, size);
        Stereogram.Save(output, Stereogram.Render(map, Options(request)), size);
        map.SavePreview(Path.ChangeExtension(output, null) + "-depth.png");
        return output;
    }

    public LabImage TestMap(string preset, out string key)
    {
        DvdGeometry geometry = DvdGeometry.Find(preset);
        TestDiscLayout layout = new();
        key = layout.Describe(geometry);
        return Map(layout, geometry);
    }

    public LabImage TiltMap(TiltRequest request, out string key)
    {
        DvdGeometry geometry = DvdGeometry.Find(request.Preset);
        TiltImageLayout layout = Tilt(request);
        key = layout.Describe(geometry);
        return Map(layout, geometry);
    }

    public string MakeTestDisc(string preset, string folder, string name,
        Action<double, string> progress, Action<string> log, CancellationToken cancel) =>
        MakeDisc(new TestDiscLayout(), DvdGeometry.Find(preset), folder, SafeName(name, "diffraction-test"), progress, log, cancel);

    public string MakeTiltDisc(TiltRequest request, string folder, string name,
        Action<double, string> progress, Action<string> log, CancellationToken cancel) =>
        MakeDisc(Tilt(request), DvdGeometry.Find(request.Preset), folder, SafeName(name, "tilt-image"), progress, log, cancel);

    public string DescribePair(StereoPairRequest request) => StereoPairLayout.Summary(request);

    public LabImage StereoPairMap(StereoPairRequest request, out string key)
    {
        DvdGeometry geometry = DvdGeometry.Find(request.Preset);
        StereoPairLayout layout = new(request);
        key = layout.Describe(geometry);
        return Map(layout, geometry);
    }

    public string MakeStereoPairDisc(StereoPairRequest request, string folder, string name,
        Action<double, string> progress, Action<string> log, CancellationToken cancel)
    {
        StereoPairLayout layout = new(request);
        return MakeDisc(layout, DvdGeometry.Find(request.Preset), folder, SafeName(name, "hologram"), progress, log, cancel,
            stem => layout.SaveViews(stem + "-views.png"));
    }

    /// <summary>Testing aid: when set, disc images stop after this many sectors.</summary>
    public uint? SectorLimit { get; set; }

    private string MakeDisc(IDiffractionLayout layout, DvdGeometry geometry, string folder, string name,
        Action<double, string> progress, Action<string> log, CancellationToken cancel, Action<string> extras = null)
    {
        string output = UniquePath(Path.Combine(Folder(folder), name + ".iso"));
        long needed = (long)(SectorLimit ?? geometry.Sectors) * 2048 + 50_000_000;
        long free = new DriveInfo(Path.GetPathRoot(output)!).AvailableFreeSpace;
        if (free < needed)
            throw new IOException($"Not enough free space in {Path.GetDirectoryName(output)}: needs {needed / 1e9:0.0} GB, has {free / 1e9:0.0} GB.");
        string stem = Path.ChangeExtension(output, null);
        File.WriteAllText(stem + "-key.txt", layout.Describe(geometry));
        DiscWriter.SaveMap(layout, geometry, stem + "-map.png");
        extras?.Invoke(stem);
        DiscWriter.Write(layout, geometry, output, 0, SectorLimit, new DiscWriter.Reporter(log, progress), cancel);
        return output;
    }

    public void ShowInFolder(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (File.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false });
        else
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = false });
        }
    }

    // ── helpers ──

    private static DepthMap Depth(StereoRequest r, int size) => r.Source switch
    {
        "depth" => DepthMap.FromImage(Existing(r.File), size, r.Invert),
        "mesh" => DepthMap.FromMesh(Existing(r.File), size, r.Yaw, r.Pitch, r.Diameter),
        _ => DepthMap.Demo(r.Demo ?? "torus", size),
    };

    private static StereogramOptions Options(StereoRequest r) => new(
        FarSeparationMm: Math.Clamp(r.Separation, 5, 40),
        DepthFactor: Math.Clamp(r.DepthFactor, .05, .6),
        DotMm: Math.Clamp(r.Dot, .02, 2),
        Seed: r.Seed,
        GuideDots: r.Guides);

    private static TiltImageLayout Tilt(TiltRequest r)
    {
        List<TiltLayerRequest> layers = r.Layers.Where(l => !string.IsNullOrEmpty(l.File)).ToList();
        if (layers.Count == 0) throw new ArgumentException("Add at least one picture.");
        return new TiltImageLayout(
            layers.Select(l => Existing(l.File)).ToList(),
            layers.Select(l => Grating.FromCode(l.Grating ?? "92")).ToList(),
            r.Threshold, r.Dither,
            layers.Select(l => l.Name ?? Path.GetFileName(l.File)).ToList());
    }

    private static string Existing(string path)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Choose a file first.");
        if (!File.Exists(path)) throw new ArgumentException("File not found: " + path);
        return path;
    }

    private static LabImage Gray(byte[] pixels, int size)
    {
        byte[] bgra = new byte[pixels.Length * 4];
        for (int i = 0; i < pixels.Length; i++)
        {
            byte v = pixels[i];
            bgra[i * 4] = v; bgra[i * 4 + 1] = v; bgra[i * 4 + 2] = v; bgra[i * 4 + 3] = 255;
        }
        return new LabImage { Width = size, Height = size, Bgra = bgra };
    }

    private static LabImage Map(IDiffractionLayout layout, DvdGeometry geometry)
    {
        (int size, byte[] bgra) = DiscWriter.RenderMap(layout, geometry);
        return new LabImage { Width = size, Height = size, Bgra = bgra };
    }

    private static string Folder(string folder)
    {
        folder = (folder ?? "").Trim().Trim('"');
        if (folder.Length == 0) throw new ArgumentException("Choose an output folder.");
        folder = Path.GetFullPath(Environment.ExpandEnvironmentVariables(folder));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string SafeName(string name, string fallback)
    {
        name = string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
        foreach (char c in Path.GetInvalidFileNameChars().Concat(['/', '\\', ':'])) name = name.Replace(c, '-');
        if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return name.Length == 0 ? fallback : name;
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path) && !File.Exists(path + ".partial")) return path;
        string stem = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path));
        for (int i = 2; ; i++)
        {
            string candidate = $"{stem}-{i}{Path.GetExtension(path)}";
            if (!File.Exists(candidate) && !File.Exists(candidate + ".partial")) return candidate;
        }
    }
}
