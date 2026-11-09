using DiscImageStudio.ImageIO;

namespace Disc3DLab;

internal sealed record StereogramOptions(
    double FarSeparationMm = 14,   // repeat width of the background pattern
    double DepthFactor = 1.0 / 3,  // classic SIRDS "mu": how far the nearest point pops out
    double DotMm = 0.15,           // random dot size; bigger dots survive blurrier burns
    int Seed = 1,
    bool GuideDots = true);

/// <summary>
/// Single-image random-dot stereogram with hidden-surface removal
/// (Thimbleby, Inglis &amp; Witten, "Displaying 3D Images: Algorithms for Single-Image
/// Random-Dot Stereograms", IEEE Computer, 1994). Output is pure black/white so the
/// disc's dithering keeps every dot exactly as drawn.
/// </summary>
internal static class Stereogram
{
    public static byte[] Render(DepthMap depth, StereogramOptions options)
    {
        int size = depth.Size;
        double pixelsPerMm = size / DepthMap.CanvasMm;
        // Far-plane separation of a SIRDS is E/2, so the virtual eye separation is twice it.
        double eye = 2 * options.FarSeparationMm * pixelsPerMm;
        double mu = options.DepthFactor;
        int dot = Math.Max(1, (int)Math.Round(options.DotMm * pixelsPerMm));
        byte[] output = new byte[size * size];

        Parallel.For(0, size, y =>
        {
            int[] same = new int[size];
            byte[] row = new byte[size];
            for (int x = 0; x < size; x++) same[x] = x;
            for (int x = 0; x < size; x++)
            {
                double z = depth[x, y];
                int separation = (int)Math.Round((1 - mu * z) * eye / (2 - mu * z));
                int left = x - (separation + (separation & y & 1)) / 2;
                int right = left + separation;
                if (left < 0 || right >= size) continue;

                // Hidden-surface removal: is this point visible to both eyes?
                bool visible;
                int t = 1;
                double zt;
                do
                {
                    zt = z + 2 * (2 - mu * z) * t / (mu * eye);
                    visible = (x - t < 0 || depth[x - t, y] < zt) && (x + t >= size || depth[x + t, y] < zt);
                    t++;
                } while (visible && zt < 1);
                if (!visible) continue;

                // Record that left and right must share a colour, merging existing chains.
                int l = same[left];
                while (l != left && l != right)
                {
                    if (l < right)
                    {
                        left = l;
                        l = same[left];
                    }
                    else
                    {
                        same[left] = right;
                        left = right;
                        l = same[left];
                        right = l;
                    }
                }
                same[left] = right;
            }
            for (int x = size - 1; x >= 0; x--)
                row[x] = same[x] == x ? Noise(x / dot, y / dot, options.Seed) : row[same[x]];
            Buffer.BlockCopy(row, 0, output, y * size, size);
        });

        if (options.GuideDots) DrawGuideDots(output, size, pixelsPerMm, options.FarSeparationMm);
        return output;
    }

    // Two dots, one far-separation apart, near the top edge of the readable ring.
    // Relax your eyes until they become three dots; the 3D image then snaps into view.
    private static void DrawGuideDots(byte[] image, int size, double pixelsPerMm, double separationMm)
    {
        double cy = -52.5;
        foreach (double cx in new[] { -separationMm / 2, separationMm / 2 })
        {
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double dx = (x + .5) / pixelsPerMm - 60 - cx, dy = (y + .5) / pixelsPerMm - 60 - cy;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d < 1.6) image[y * size + x] = d < 1.0 ? (byte)0 : (byte)255;
            }
        }
    }

    private static byte Noise(int x, int y, int seed)
    {
        ulong h = (ulong)(uint)x * 0x9E3779B97F4A7C15UL ^ (ulong)(uint)y * 0xC2B2AE3D27D4EB4FUL ^ (ulong)(uint)seed * 0x165667B19E3779F9UL;
        h ^= h >> 33; h *= 0xFF51AFD7ED558CCDUL; h ^= h >> 33; h *= 0xC4CEB9FE1A85EC53UL; h ^= h >> 33;
        return (h & 1) == 0 ? (byte)0 : (byte)255;
    }

    public static void Save(string path, byte[] pixels, int size) => RasterCodec.SavePng(path, size, size, pixels, grayscale: true);

    /// <summary>Recovers the hidden depth by matching each pixel against the pattern one repeat
    /// to its left. Used to check that a stereogram really encodes the intended shape.</summary>
    public static float[] DecodeDisparity(byte[] image, int size, int minSep, int maxSep, int window = 7)
    {
        float[] result = new float[size * size];
        Parallel.For(window, size - window, y =>
        {
            for (int x = maxSep + window; x < size - window; x++)
            {
                int best = minSep, bestScore = int.MaxValue;
                for (int s = minSep; s <= maxSep; s++)
                {
                    int score = 0;
                    for (int dy = -window; dy <= window; dy += 2)
                    for (int dx = -window; dx <= window; dx++)
                        score += image[(y + dy) * size + x + dx] != image[(y + dy) * size + x + dx - s] ? 1 : 0;
                    if (score < bestScore) { bestScore = score; best = s; }
                }
                result[y * size + x] = best;
            }
        });
        return result;
    }
}
