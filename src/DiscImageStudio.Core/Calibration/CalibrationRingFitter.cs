using System.Text.Json.Serialization;

namespace DiscImageStudio.Core.Calibration;

/// <summary>Zero-based index among complete engraved rings, ordered from the centre outwards.</summary>
public sealed record CalibrationRing(int RingIndex, double RadiusMm, double WidthMm)
{
    public double InnerRadiusMm => RadiusMm - WidthMm / 2;
}

/// <summary>Radius of one observed edge on the rectified photo. For wide bands, IDs 0..2
/// remain the historical inner edges; IDs 3..5 are the corresponding outer edges.</summary>
public sealed record CalibrationRingObservation(int RingIndex, double RadiusMm,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<CalibrationPoint>? Points = null);

public static partial class CalibrationRingFitter
{
    public static int MeasurementCount(CalibrationTarget target) =>
        target.HasWideAddressBands ? 2 * target.VisibleRings.Count : target.VisibleRings.Count;

    /// <summary>The rectified disc centre is already known. Its least-squares radius is
    /// the mean point distance, even when all three clicks are on the same side of the disc.</summary>
    public static double MeasureArc(IReadOnlyList<CalibrationPoint> points, double physicalOuterRadiusMm)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count != 3)
            throw new ArgumentException("Place three points along the inner edge of the same ring.");
        if (!double.IsFinite(physicalOuterRadiusMm) || physicalOuterRadiusMm <= 7.5)
            throw new ArgumentOutOfRangeException(nameof(physicalOuterRadiusMm));
        Span<double> radii = stackalloc double[3];
        Span<double> angles = stackalloc double[3];
        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            double radius = p is null ? double.NaN : double.Hypot(p.X, p.Y);
            if (!double.IsFinite(radius) || radius < 7.5 || radius > physicalOuterRadiusMm)
                throw new ArgumentException("Place each arc point between the centre hole and the outer disc edge.");
            radii[i] = radius;
            angles[i] = Math.Atan2(p!.Y, p.X);
        }
        double largestSeparation = 0;
        for (int i = 1; i < angles.Length; i++)
            for (int j = 0; j < i; j++)
            {
                double angle = Math.Abs(Math.IEEERemainder(angles[i] - angles[j], Math.Tau));
                if (angle < Math.PI / 180)
                    throw new ArgumentException("The three points are too close together. Space them farther apart along this arc.");
                largestSeparation = Math.Max(largestSeparation, angle);
            }
        if (largestSeparation < 6 * Math.PI / 180)
            throw new ArgumentException("The three points are too close together. Space them farther apart along this arc.");
        radii.Sort();
        if (radii[2] - radii[0] > 0.5)
            throw new ArgumentException("The three radii differ too much. Reposition the points on the inner edge of the same ring.");
        return (radii[0] + radii[1] + radii[2]) / 3;
    }

    // Partial drafts, including crossed arcs that are still being edited, can be saved.
    public static void ValidateObservations(CalibrationTarget target, IReadOnlyList<CalibrationRingObservation> observations,
        double physicalOuterRadiusMm = 1000)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(observations);
        if (!double.IsFinite(physicalOuterRadiusMm) || physicalOuterRadiusMm <= 7.5)
            throw new ArgumentOutOfRangeException(nameof(physicalOuterRadiusMm));
        var indices = new HashSet<int>();
        int count = MeasurementCount(target);
        if (observations.Count > count || observations.Any(p => p is null
            || (uint)p.RingIndex >= (uint)count || !indices.Add(p.RingIndex)
            || !double.IsFinite(p.RadiusMm) || p.RadiusMm <= 0 || p.RadiusMm > physicalOuterRadiusMm))
            throw new ArgumentException("The ring number or measured radius is invalid.");
        foreach (var p in observations)
        {
            if (p.Points is null) continue;
            double measured;
            if (target.HasWideAddressBands && p.Points.Count == 1)
            {
                var point = p.Points[0];
                measured = point is null ? double.NaN : double.Hypot(point.X, point.Y);
                if (!double.IsFinite(measured) || measured < 7.5 || measured > Math.Min(60, physicalOuterRadiusMm))
                    throw new ArgumentException("Place each arc point between the centre hole and the outer disc edge.");
            }
            else measured = MeasureArc(p.Points, physicalOuterRadiusMm);
            if (Math.Abs(measured - p.RadiusMm) > 1e-6)
                throw new ArgumentException("The saved arc points do not match the measured radius.");
        }
    }

    /// <summary>Fit the two full-track endpoint radii while preserving the generation snapshot's
    /// sector count, linear density, DVD pitch shape and BD recording range. Each source circle
    /// fixes a track progress; no angular phase, rotation or turn count is observed.</summary>
    public static CalibrationFitResult Fit(CalibrationTarget target, IReadOnlyList<CalibrationRingObservation> observations,
        CancellationToken cancellationToken = default)
    {
        ValidateObservations(target, observations);
        cancellationToken.ThrowIfCancellationRequested();
        CalibrationParameters parameters = target.ParametersForGeometry(target.Parameters);
        CalibrationRingObservation[] ordered;
        double[] progress;
        if (target.HasWideAddressBands)
        {
            if (!HasRequiredWideBandEdges(target, observations.Select(p => p.RingIndex)))
                return Failed(target.SchemaVersion == 11 && observations.All(p => p.RingIndex < target.VisibleRings.Count)
                    ? "Measure the edge facing the disc centre on both the innermost and outermost bands."
                    : "Measure both edges of dark bands 1 and 3.");
            ordered = observations.OrderBy(p => WideEdgeProgress(target, p.RingIndex)).ToArray();
            progress = ordered.Select(p => WideEdgeProgress(target, p.RingIndex)).ToArray();
        }
        else
        {
            if (observations.Count < 3) return Failed("Align at least three rings, including inner and outer rings.");
            ordered = observations.OrderBy(p => p.RingIndex).ToArray();
            double[] source = ordered.Select(p => target.VisibleRings[p.RingIndex].InnerRadiusMm).ToArray();
            if (source[^1] - source[0] < 0.30 * (parameters.OuterRadiusMm - parameters.InnerRadiusMm))
                return Failed("The selected rings are too close together. Include a ring nearer the inner or outer edge.");
            progress = source.Select(new CalibrationGeometry(parameters).ProgressAtRadius).ToArray();
        }

        for (int i = 1; i < ordered.Length; i++)
            if (ordered[i].RadiusMm <= ordered[i - 1].RadiusMm)
                return Failed(target.HasWideAddressBands ? "The radial order is inconsistent. Check the band numbers and sides."
                    : "The measured rings are out of order. Check their numbers or reposition the arcs.");
        if (target.HasWideAddressBands)
        {
            // Compare every interior observation with the prediction from the extreme
            // measured edges alone. Only absolute edge radii enter the fit below.
            for (int i = 1; i < ordered.Length - 1; i++)
            {
                double predicted = InterpolateWideRadius(progress[i], progress[0], ordered[0].RadiusMm,
                    progress[^1], ordered[^1].RadiusMm);
                if (Math.Abs(ordered[i].RadiusMm - predicted) > .40)
                    return Failed(target.SchemaVersion == 11 && ordered[^1].RingIndex == target.VisibleRings.Count - 1
                        ? "The middle edge differs by over 0.40 mm from its position predicted by the other two. Check the band numbers, the inner edges and photo rectification."
                        : "A boundary differs by over 0.40 mm from the position predicted by the others. Check band IDs and edge readings.");
            }
        }
        double[] Residuals(CalibrationParameters candidate)
        {
            var geometry = new CalibrationGeometry(candidate);
            return progress.Select((q, i) => geometry.AtProgress(q).Radius - ordered[i].RadiusMm).ToArray();
        }

        // Only two parameters vary. Using the physical forward model here also works with
        // nonzero saved cubic pitch and with short spirals where the thin-track formula drifts.
        const string invalid = "The rings did not yield valid inner and outer radii. Check the selected arcs.";
        double[] residual = Residuals(parameters);
        double cost = residual.Sum(r => r * r);
        for (int iteration = 0; iteration < 30; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double band = parameters.OuterRadiusMm - parameters.InnerRadiusMm;
            double h = Math.Min(0.0001, Math.Min(band, parameters.TrackLengthMm - band) / 4);
            // Step inwards at the BD physical limit and keep each finite-difference geometry valid.
            double ho = parameters.Kind == CalibrationDiscKind.BluRay && parameters.OuterRadiusMm + h > 60 ? -h : h;
            double[] ri = Residuals(parameters with { InnerRadiusMm = parameters.InnerRadiusMm + h });
            double[] ro = Residuals(parameters with { OuterRadiusMm = parameters.OuterRadiusMm + ho });
            double aa = 0, ab = 0, bb = 0, ar = 0, br = 0;
            for (int i = 0; i < residual.Length; i++)
            {
                double a = (ri[i] - residual[i]) / h, b = (ro[i] - residual[i]) / ho;
                aa += a * a;
                ab += a * b;
                bb += b * b;
                ar += a * residual[i];
                br += b * residual[i];
            }
            double det = aa * bb - ab * ab;
            if (!double.IsFinite(det) || det <= 1e-12 * aa * bb) return Failed(invalid);
            double innerStep = (ab * br - bb * ar) / det;
            double outerStep = (ab * ar - aa * br) / det;
            double largestStep = Math.Max(Math.Abs(innerStep), Math.Abs(outerStep));
            if (!double.IsFinite(largestStep)) return Failed(invalid);
            if (largestStep < 1e-8) break;

            bool improved = false;
            for (double fraction = 1; fraction >= 1.0 / 1024; fraction /= 2)
            {
                var next = parameters with
                {
                    InnerRadiusMm = parameters.InnerRadiusMm + fraction * innerStep,
                    OuterRadiusMm = parameters.OuterRadiusMm + fraction * outerStep,
                };
                if (next.InnerRadiusMm <= 0 || next.OuterRadiusMm <= next.InnerRadiusMm
                    || next.OuterRadiusMm - next.InnerRadiusMm >= next.TrackLengthMm
                    || (next.Kind == CalibrationDiscKind.BluRay && next.OuterRadiusMm > 60))
                    continue;
                double[] trial = Residuals(next);
                double newCost = trial.Sum(r => r * r);
                if (!double.IsFinite(newCost) || newCost >= cost) continue;
                (parameters, residual, cost) = (next, trial, newCost);
                improved = true;
                break;
            }
            if (!improved)
            {
                // Noisy measurements have a nonzero minimum. The last derivative
                // can request a few nanometres whose cost difference rounds away.
                if (largestStep < 1e-5) break;
                return Failed(invalid);
            }
        }
        double rms = Math.Sqrt(cost / residual.Length);
        if (target.HasWideAddressBands && (parameters.InnerRadiusMm < 7.5 || parameters.OuterRadiusMm > 60 + 1e-8))
            return Failed("The estimated radii fall outside the disc. Check the photo scale and the marked boundaries.");
        var candidate = new CalibrationFitCandidate(parameters, 0, rms, cost, residual.Max(r => Math.Abs(r)));
        return new(true, parameters, 0, rms, [candidate], []);
    }

    private static CalibrationFitResult Failed(string warning) =>
        new(false, null, 0, double.PositiveInfinity, [], [warning]);
}
