using System.Globalization;
using DiscImageStudio.ImageIO;

namespace Disc3DLab;

/// <summary>A square depth field covering the 120 mm disc canvas. 0 = far (background), 1 = nearest.</summary>
internal sealed class DepthMap
{
    public const double CanvasMm = 120;
    public int Size { get; }
    public float[] Z { get; }

    public DepthMap(int size)
    {
        Size = size;
        Z = new float[size * size];
    }

    public float this[int x, int y] => Z[y * Size + x];

    /// <summary>Millimetre coordinates (centre 0,0; x right, y down) for pixel centres.</summary>
    public double Mm(int pixel) => (pixel + .5) * CanvasMm / Size - CanvasMm / 2;

    public static DepthMap FromImage(string path, int size, bool invert)
    {
        BgraRaster raster = RasterCodec.LoadBgra32(path, 512L * 1024 * 1024, 200_000_000);
        DepthMap map = new(size);
        // "Contain" fit: the whole picture is visible, centred on the disc.
        double scale = Math.Min((double)size / raster.Width, (double)size / raster.Height);
        double offsetX = (size - raster.Width * scale) / 2, offsetY = (size - raster.Height * scale) / 2;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            double sx = (x + .5 - offsetX) / scale - .5, sy = (y + .5 - offsetY) / scale - .5;
            if (sx < -.5 || sy < -.5 || sx > raster.Width - .5 || sy > raster.Height - .5) continue;
            float luminance = Bilinear(raster, sx, sy);
            map.Z[y * size + x] = invert ? 1 - luminance : luminance;
        }
        return map;
    }

    private static float Bilinear(BgraRaster raster, double x, double y)
    {
        int x0 = Math.Clamp((int)Math.Floor(x), 0, raster.Width - 1), y0 = Math.Clamp((int)Math.Floor(y), 0, raster.Height - 1);
        int x1 = Math.Min(x0 + 1, raster.Width - 1), y1 = Math.Min(y0 + 1, raster.Height - 1);
        double fx = Math.Clamp(x - x0, 0, 1), fy = Math.Clamp(y - y0, 0, 1);
        double top = Lum(raster, x0, y0) * (1 - fx) + Lum(raster, x1, y0) * fx;
        double bottom = Lum(raster, x0, y1) * (1 - fx) + Lum(raster, x1, y1) * fx;
        return (float)(top * (1 - fy) + bottom * fy);
    }

    private static double Lum(BgraRaster raster, int x, int y)
    {
        int o = (y * raster.Width + x) * 4;
        double alpha = raster.Pixels[o + 3] / 255.0;
        double value = (.0722 * raster.Pixels[o] + .7152 * raster.Pixels[o + 1] + .2126 * raster.Pixels[o + 2]) / 255.0;
        return value * alpha; // transparent = background (far)
    }

    /// <summary>Built-in shapes, sized to sit in the readable ring of the disc.</summary>
    public static DepthMap Demo(string name, int size)
    {
        DepthMap map = new(size);
        for (int y = 0; y < size; y++)
        {
            double my = map.Mm(y);
            for (int x = 0; x < size; x++)
            {
                double mx = map.Mm(x);
                double r = Math.Sqrt(mx * mx + my * my);
                double angle = Math.Atan2(my, mx);
                double z = name switch
                {
                    // A doughnut lying in the readable band (radius ~41 mm, tube ~13 mm).
                    "torus" => Dome(Math.Sqrt(Math.Pow(r - 41, 2)), 13),
                    // Three stepped terraces rising toward the outside edge.
                    "rings" => r < 26 || r > 57 ? 0 : r < 36 ? .33 : r < 47 ? .66 : 1,
                    // A ring of eight spheres orbiting the hole.
                    "spheres" => Enumerable.Range(0, 8).Max(i =>
                    {
                        double a = i * Math.PI / 4, cx = 41 * Math.Cos(a), cy = 41 * Math.Sin(a);
                        return Dome(Math.Sqrt((mx - cx) * (mx - cx) + (my - cy) * (my - cy)), 9);
                    }),
                    // A wavy ring like a ripple in water.
                    "ripple" => r < 25 || r > 57 ? 0 : .5 + .5 * Math.Sin((r - 25) / 32 * 6 * Math.PI) * Math.Sin(Math.PI * (r - 25) / 32),
                    // A five-pointed star whose arms sit in the readable band.
                    "star" => Star(r, angle),
                    _ => throw new ArgumentException($"Unknown demo '{name}'. Choose torus, rings, spheres, ripple or star."),
                };
                map.Z[y * size + x] = (float)Math.Clamp(z, 0, 1);
            }
        }
        return map;
    }

    public static readonly string[] DemoNames = ["torus", "rings", "spheres", "ripple", "star"];

    private static double Dome(double distance, double radius) =>
        distance >= radius ? 0 : Math.Sqrt(1 - distance * distance / (radius * radius));

    private static double Star(double r, double angle)
    {
        if (r < 25 || r > 57) return 0;
        double arm = Math.Cos(5 * (angle + Math.PI / 2));
        double edge = 36 + 20 * Math.Pow((arm + 1) / 2, 2);
        return r < edge ? Math.Clamp((edge - r) / 6, 0, 1) * .9 + .1 : 0;
    }

    /// <summary>Rasterizes an OBJ or STL mesh into a depth map (orthographic, viewed from the front).</summary>
    public static DepthMap FromMesh(string path, int size, double yawDegrees, double pitchDegrees, double diameterMm)
    {
        (List<(double X, double Y, double Z)> vertices, List<(int A, int B, int C)> triangles) = LoadMesh(path);

        double yaw = yawDegrees * Math.PI / 180, pitch = pitchDegrees * Math.PI / 180;
        var rotated = vertices.Select(v =>
        {
            double x = v.X * Math.Cos(yaw) + v.Z * Math.Sin(yaw);
            double z = -v.X * Math.Sin(yaw) + v.Z * Math.Cos(yaw);
            double y = v.Y * Math.Cos(pitch) - z * Math.Sin(pitch);
            z = v.Y * Math.Sin(pitch) + z * Math.Cos(pitch);
            return (X: x, Y: y, Z: z);
        }).ToArray();

        double minX = rotated.Min(v => v.X), maxX = rotated.Max(v => v.X);
        double minY = rotated.Min(v => v.Y), maxY = rotated.Max(v => v.Y);
        double minZ = rotated.Min(v => v.Z), maxZ = rotated.Max(v => v.Z);
        double extent = Math.Max(maxX - minX, maxY - minY);
        if (extent <= 0) throw new ArgumentException("The mesh is flat in the viewing direction.");
        double pixelsPerUnit = diameterMm / extent * size / CanvasMm;
        double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2, depthRange = Math.Max(maxZ - minZ, 1e-9);

        DepthMap map = new(size);
        float[] buffer = map.Z;
        Array.Fill(buffer, -1f);
        foreach ((int a, int b, int c) in triangles)
        {
            var p = new[] { rotated[a], rotated[b], rotated[c] }
                .Select(v => (X: size / 2.0 + (v.X - cx) * pixelsPerUnit,
                              Y: size / 2.0 - (v.Y - cy) * pixelsPerUnit,     // image y runs down
                              Z: (v.Z - minZ) / depthRange)).ToArray();
            int x0 = Math.Max(0, (int)Math.Floor(p.Min(q => q.X))), x1 = Math.Min(size - 1, (int)Math.Ceiling(p.Max(q => q.X)));
            int y0 = Math.Max(0, (int)Math.Floor(p.Min(q => q.Y))), y1 = Math.Min(size - 1, (int)Math.Ceiling(p.Max(q => q.Y)));
            double area = (p[1].X - p[0].X) * (p[2].Y - p[0].Y) - (p[2].X - p[0].X) * (p[1].Y - p[0].Y);
            if (Math.Abs(area) < 1e-12) continue;
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                double px = x + .5, py = y + .5;
                double w0 = ((p[1].X - px) * (p[2].Y - py) - (p[2].X - px) * (p[1].Y - py)) / area;
                double w1 = ((p[2].X - px) * (p[0].Y - py) - (p[0].X - px) * (p[2].Y - py)) / area;
                double w2 = 1 - w0 - w1;
                if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                float z = (float)(w0 * p[0].Z + w1 * p[1].Z + w2 * p[2].Z);
                int index = y * size + x;
                if (z > buffer[index]) buffer[index] = z;
            }
        }
        // Background is far (0); the mesh spans 0.15..1 so its back still stands off the background.
        for (int i = 0; i < buffer.Length; i++) buffer[i] = buffer[i] < 0 ? 0 : .15f + .85f * buffer[i];
        return map;
    }

    /// <summary>Reads an OBJ or STL mesh.</summary>
    internal static (List<(double X, double Y, double Z)> Vertices, List<(int A, int B, int C)> Triangles) LoadMesh(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) throw new ArgumentException("Choose the 3D model first.");
        List<(double X, double Y, double Z)> vertices = [];
        List<(int A, int B, int C)> triangles = [];
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".obj") ReadObj(path, vertices, triangles);
        else if (extension == ".stl") ReadStl(path, vertices, triangles);
        else throw new ArgumentException("Mesh must be .obj or .stl (export GLB/PLY to OBJ first).");
        if (triangles.Count == 0) throw new ArgumentException("The mesh contains no triangles.");
        return (vertices, triangles);
    }

    private static void ReadObj(string path, List<(double, double, double)> vertices, List<(int, int, int)> triangles)
    {
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (line.StartsWith("v ", StringComparison.Ordinal))
            {
                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                vertices.Add((Parse(parts[1]), Parse(parts[2]), Parse(parts[3])));
            }
            else if (line.StartsWith("f ", StringComparison.Ordinal))
            {
                int[] face = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1)
                    .Select(token => int.Parse(token.Split('/')[0], CultureInfo.InvariantCulture))
                    .Select(index => index < 0 ? vertices.Count + index : index - 1).ToArray();
                for (int i = 1; i + 1 < face.Length; i++) triangles.Add((face[0], face[i], face[i + 1]));
            }
        }
    }

    private static void ReadStl(string path, List<(double, double, double)> vertices, List<(int, int, int)> triangles)
    {
        byte[] data = File.ReadAllBytes(path);
        bool binary = data.Length >= 84 && 84 + 50L * BitConverter.ToUInt32(data, 80) == data.Length;
        if (binary)
        {
            uint count = BitConverter.ToUInt32(data, 80);
            for (int t = 0; t < count; t++)
            {
                int offset = 84 + t * 50 + 12;
                for (int k = 0; k < 3; k++, offset += 12)
                    vertices.Add((BitConverter.ToSingle(data, offset), BitConverter.ToSingle(data, offset + 4), BitConverter.ToSingle(data, offset + 8)));
                triangles.Add((vertices.Count - 3, vertices.Count - 2, vertices.Count - 1));
            }
            return;
        }
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (!line.StartsWith("vertex ", StringComparison.OrdinalIgnoreCase)) continue;
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            vertices.Add((Parse(parts[1]), Parse(parts[2]), Parse(parts[3])));
            if (vertices.Count % 3 == 0) triangles.Add((vertices.Count - 3, vertices.Count - 2, vertices.Count - 1));
        }
    }

    private static double Parse(string text) => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    public void SavePreview(string path)
    {
        byte[] pixels = new byte[Size * Size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)Math.Round(Math.Clamp(Z[i], 0, 1) * 255);
        RasterCodec.SavePng(path, Size, Size, pixels, grayscale: true);
    }
}
