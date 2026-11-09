using System.Diagnostics;
using System.Security.Cryptography;
using DiscImageStudio.Core;
using DiscImageStudio.ImageIO;
using DvdImageSolver.Solver;

namespace Disc3DLab;

/// <summary>Writes a complete raw DVD sector image whose payload bytes follow a diffraction layout.</summary>
internal static class DiscWriter
{
    /// <summary>Progress sink so both the console and the window can follow a long write.</summary>
    public sealed record Reporter(Action<string> Log, Action<double, string> Progress)
    {
        public static readonly Reporter Console = new(System.Console.WriteLine, (fraction, text) =>
            System.Console.Write($"\r  {text}   "));
    }

    public static void Write(IDiffractionLayout layout, DvdGeometry geometry, string output, int parallelism,
        uint? sectorLimit = null, Reporter? reporter = null, CancellationToken cancellationToken = default)
    {
        reporter ??= Reporter.Console;
        if (geometry.Sectors % 16 != 0) throw new ArgumentException("DVD sector count must be a multiple of 16.");
        uint sectors = sectorLimit ?? geometry.Sectors;
        string partial = output + ".partial";
        foreach (string path in new[] { output, partial }) if (File.Exists(path)) throw new IOException("Already exists: " + path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);

        // Pre-verify on a few sample tracks before spending minutes on the full image.
        reporter.Progress(0, "Checking the layout on sample tracks...");
        reporter.Log(Verify(layout, geometry, null));

        Stopwatch clock = Stopwatch.StartNew();
        double lastReport = -10;
        try
        {
            using (FileStream stream = new(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20))
            {
                FastDispersionImageWriter.WriteScrambledCodewordPatternStream(
                    sample => Sample(layout, sample), stream, geometry.Options(0, sectors, parallelism), progress =>
                    {
                        if (progress.Elapsed.TotalSeconds - lastReport < 1 && progress.CompletedBlocks != progress.TotalBlocks) return;
                        lastReport = progress.Elapsed.TotalSeconds;
                        double fraction = progress.Fraction;
                        double eta = fraction > 0 ? progress.Elapsed.TotalSeconds * (1 - fraction) / fraction : 0;
                        reporter.Progress(fraction,
                            $"{fraction,6:P1}  {progress.BytesWritten / 1048576.0:N0} MiB  elapsed {progress.Elapsed:mm\\:ss}  left ~{TimeSpan.FromSeconds(eta):mm\\:ss}");
                    }, cancellationToken);
                stream.Flush(true);
            }
            if (ReferenceEquals(reporter, Reporter.Console)) System.Console.WriteLine();
            cancellationToken.ThrowIfCancellationRequested();
            if (sectorLimit is null)
            {
                reporter.Progress(1, "Verifying the finished image...");
                using FileStream check = File.OpenRead(partial);
                if (check.Length != (long)geometry.Sectors * 2048) throw new IOException("The image length is wrong.");
                reporter.Log(Verify(layout, geometry, check));
            }
            File.Move(partial, output);
            reporter.Log($"Done in {clock.Elapsed:mm\\:ss}: {output}");
        }
        catch
        {
            if (File.Exists(partial)) File.Delete(partial);
            throw;
        }
    }

    private static byte Sample(IDiffractionLayout layout, DvdCodewordSample sample) =>
        layout.Gratings[layout.GratingAt(sample.Xmm, sample.Ymm)].ByteAt(sample.GlobalCodewordIndex);

    /// <summary>
    /// Regenerates short stretches at several radii independently and checks every target
    /// byte by undoing the DVD scrambler, so a corrupted or misaligned image is caught.
    /// With <paramref name="actual"/> null it only checks that the layout is reachable on the disc.
    /// </summary>
    public static string Verify(IDiffractionLayout layout, DvdGeometry g, FileStream? actual)
    {
        DvdTrackGeometry track = DvdTrackGeometry.Create(g.InnerMm, g.OuterMm, g.Sectors * 38688d * g.BitNm * 1e-6,
            g.PitchLinear, g.PitchQuadratic, g.PitchCubic);
        Dictionary<string, long> counts = new();
        // Centre of each test ring, plus the outer edge: every texture gets checked.
        double[] radii = Enumerable.Range(0, 12)
            .Select(i => TestDiscLayout.FirstRadius + i * TestDiscLayout.RingPitch + (TestDiscLayout.RingPitch - TestDiscLayout.Gap) / 2)
            .Append(g.OuterMm - 1).Where(r => r > g.InnerMm + .3 && r < g.OuterMm - .3).ToArray();
        foreach (double radius in radii)
        {
            uint start = Math.Min((uint)(track.ArcLengthAtRadius(radius) / (38688 * g.BitNm * 1e-6)) / 16 * 16, g.Sectors - 64);
            List<byte> expected = new(64 * 2048);
            using MemoryStream fragment = new();
            FastDispersionImageWriter.WriteScrambledCodewordPatternStream(sample =>
            {
                byte q = Sample(layout, sample);
                expected.Add(q);
                string code = layout.Gratings[layout.GratingAt(sample.Xmm, sample.Ymm)].Code;
                counts[code] = counts.GetValueOrDefault(code) + 1;
                return q;
            }, fragment, g.Options(start, 64, 1));
            byte[] data = fragment.ToArray();
            if (data.Length != 64 * 2048 || expected.Count != data.Length)
                throw new IOException($"Fragment at r={radius} mm has {data.Length} bytes for {expected.Count} samples.");
            if (actual is not null)
            {
                actual.Position = (long)start * 2048;
                actual.ReadExactly(data);
            }
            ushort[] presets = [1, 0x5500, 2, 0x2A00, 4, 0x5400, 8, 0x2800, 0x10, 0x5000, 0x20, 0x2001, 0x40, 0x4002, 0x80, 5];
            for (int sector = 0; sector < 64; sector++)
            {
                int state = presets[((0x30000 + start + sector) >> 4) & 15];
                for (int i = 0; i < 2048; i++)
                {
                    int index = sector * 2048 + i;
                    byte q = (byte)(data[index] ^ (state & 255));
                    if (q != expected[index]) throw new IOException($"Wrong target byte at sector {start + sector}, byte {i}.");
                    for (int bit = 0; bit < 8; bit++) state = ((state << 1) | (((state >> 14) ^ (state >> 10)) & 1)) & 0x7FFF;
                }
            }
        }
        return "Verified sample tracks: " + string.Join(", ", counts.OrderBy(c => c.Key).Select(c => $"{c.Key}={c.Value:N0}"));
    }

    /// <summary>False-colour map of which grating sits where (not a prediction of how it looks).</summary>
    public static (int Size, byte[] Bgra) RenderMap(IDiffractionLayout layout, DvdGeometry g)
    {
        const int size = 1500;
        int[] palette = [0xE6194B, 0xF58231, 0xFFE119, 0xBFEF45, 0x3CB44B, 0x42D4F4, 0x4363D8, 0x911EB4, 0xF032E6, 0xA9A9A9, 0x9A6324, 0x404040, 0x000000, 0xFFFFFF];
        byte[] pixels = new byte[size * size * 4];
        Parallel.For(0, size, y =>
        {
            for (int x = 0; x < size; x++)
            {
                double mx = (x + .5) * 120 / size - 60, my = (y + .5) * 120 / size - 60;
                double r = Math.Sqrt(mx * mx + my * my);
                int color;
                if (r < g.InnerMm || r > g.OuterMm) color = -1; // transparent
                else
                {
                    int index = layout.GratingAt(mx, my);
                    Grating grating = layout.Gratings[index];
                    color = grating.Code == "RND" ? 0x5A5A5A
                        : grating.Code == "A5" && index >= Grating.TestRings.Length ? 0x000000
                        : grating.Code == "92" && index >= Grating.TestRings.Length ? 0xFFFFFF
                        : palette[index % palette.Length];
                }
                int o = (y * size + x) * 4;
                if (color < 0) continue;
                pixels[o] = (byte)color; pixels[o + 1] = (byte)(color >> 8); pixels[o + 2] = (byte)(color >> 16); pixels[o + 3] = 255;
            }
        });
        return (size, pixels);
    }

    public static void SaveMap(IDiffractionLayout layout, DvdGeometry g, string path)
    {
        (int size, byte[] pixels) = RenderMap(layout, g);
        // Files get a light background instead of transparency so they read well anywhere.
        for (int o = 0; o < pixels.Length; o += 4)
            if (pixels[o + 3] == 0) { pixels[o] = 0xF0; pixels[o + 1] = 0xEB; pixels[o + 2] = 0xE7; pixels[o + 3] = 255; }
        RasterCodec.SavePng(path, size, size, pixels, grayscale: false);
    }

    public static string Sha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
