#nullable disable
namespace Disc3DLab;

/// <summary>
/// Renders a lit, gray picture of an OBJ/STL model from a chosen angle, for Hologram mode.
/// Every view uses the same centre and scale (the model's bounding sphere), so the views line up
/// and only the turn between them changes. The background is black, so it stays dark on the disc.
/// </summary>
internal sealed class ModelRender
{
    private readonly (double X, double Y, double Z)[] _vertices;
    private readonly (int A, int B, int C)[] _triangles;
    private readonly double _radius;

    public ModelRender(string path)
    {
        (List<(double X, double Y, double Z)> vertices, List<(int A, int B, int C)> triangles) = DepthMap.LoadMesh(path);
        double cx = (vertices.Min(v => v.X) + vertices.Max(v => v.X)) / 2;
        double cy = (vertices.Min(v => v.Y) + vertices.Max(v => v.Y)) / 2;
        double cz = (vertices.Min(v => v.Z) + vertices.Max(v => v.Z)) / 2;
        _vertices = vertices.Select(v => (v.X - cx, v.Y - cy, v.Z - cz)).ToArray();
        _triangles = triangles.ToArray();
        _radius = _vertices.Max(v => Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z));
        if (_radius <= 0) throw new ArgumentException("The model has no size.");
    }

    /// <summary>
    /// A size x size picture (values 0..1, grid size stored in the last two entries) of the model
    /// turned by yaw (left/right) and pitch (up/down), lit from the upper left.
    /// </summary>
    public double[] Render(int size, double yawDegrees, double pitchDegrees, double zoom = 1) =>
        Render(size, yawDegrees, pitchDegrees, zoom, null);

    /// <summary>As above; also fills depth (size x size, 1 = nearest, 0 = background) if given.</summary>
    public double[] Render(int size, double yawDegrees, double pitchDegrees, double zoom, double[] depth)
    {
        double yaw = yawDegrees * Math.PI / 180, pitch = pitchDegrees * Math.PI / 180;
        double cyaw = Math.Cos(yaw), syaw = Math.Sin(yaw), cp = Math.Cos(pitch), sp = Math.Sin(pitch);
        double scale = size / (2.04 * _radius) * Math.Clamp(zoom, .2, 5);
        var p = new (double X, double Y, double Z)[_vertices.Length];
        for (int i = 0; i < p.Length; i++)
        {
            (double x0, double y0, double z0) = _vertices[i];
            double x = x0 * cyaw + z0 * syaw;
            double z = -x0 * syaw + z0 * cyaw;
            double y = y0 * cp - z * sp;
            z = y0 * sp + z * cp;
            p[i] = (size / 2.0 + x * scale, size / 2.0 - y * scale, z); // image y runs down; larger z = nearer
        }

        (double X, double Y, double Z) light = Normalize((-.45, .55, .7));
        double[] image = new double[size * size + 2];
        double[] zbuffer = new double[size * size];
        Array.Fill(zbuffer, double.NegativeInfinity);
        foreach ((int ia, int ib, int ic) in _triangles)
        {
            var a = p[ia]; var b = p[ib]; var c = p[ic];
            // Face normal in view space (y flipped back to up), facing the viewer.
            (double X, double Y, double Z) u = (b.X - a.X, -(b.Y - a.Y), (b.Z - a.Z) * scale);
            (double X, double Y, double Z) v = (c.X - a.X, -(c.Y - a.Y), (c.Z - a.Z) * scale);
            (double X, double Y, double Z) n = Normalize((u.Y * v.Z - u.Z * v.Y, u.Z * v.X - u.X * v.Z, u.X * v.Y - u.Y * v.X));
            if (n.Z < 0) n = (-n.X, -n.Y, -n.Z);
            double shade = .14 + .86 * Math.Max(0, n.X * light.X + n.Y * light.Y + n.Z * light.Z);

            double area = (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y);
            if (Math.Abs(area) < 1e-12) continue;
            int x0 = Math.Max(0, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X)))), x1 = Math.Min(size - 1, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
            int y0 = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y)))), y1 = Math.Min(size - 1, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                double px = x + .5, py = y + .5;
                double w0 = ((b.X - px) * (c.Y - py) - (c.X - px) * (b.Y - py)) / area;
                double w1 = ((c.X - px) * (a.Y - py) - (a.X - px) * (c.Y - py)) / area;
                double w2 = 1 - w0 - w1;
                if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                double z = w0 * a.Z + w1 * b.Z + w2 * c.Z;
                int index = y * size + x;
                if (z <= zbuffer[index]) continue;
                zbuffer[index] = z;
                image[index] = shade;
            }
        }
        image[^2] = size; image[^1] = size;
        if (depth != null)
        {
            // Normalise to the model's own depth range: near = white, far = dark, background black.
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (double z in zbuffer) if (!double.IsNegativeInfinity(z)) { lo = Math.Min(lo, z); hi = Math.Max(hi, z); }
            double range = Math.Max(1e-9, hi - lo);
            for (int i = 0; i < zbuffer.Length; i++)
                depth[i] = double.IsNegativeInfinity(zbuffer[i]) ? 0 : .2 + .8 * (zbuffer[i] - lo) / range;
        }
        return image;
    }

    private static (double X, double Y, double Z) Normalize((double X, double Y, double Z) v)
    {
        double length = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return length < 1e-15 ? (0, 0, 1) : (v.X / length, v.Y / length, v.Z / length);
    }
}
