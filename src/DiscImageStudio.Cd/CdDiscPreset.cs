namespace DiscImageStudio.Cd;

public sealed record CdDiscPreset(
    string Id,
    string DisplayName,
    string Description,
    long Sectors,
    double InnerRadiusMm,
    double OuterRadiusMm,
    string? DisplayNameResourceKey = null,
    string? DescriptionResourceKey = null,
    bool IsCustom = false,
    string? Contributor = null,
    double PitchLinear = 0,
    double PitchQuadratic = 0,
    double PitchCubic = 0)
{
    public static CdDiscPreset Manual { get; } = new(
        "__manual__",
        "Custom parameters (unsaved)",
        "Generation parameters have been adjusted manually. Save them as a user preset to keep them.",
        359_849,
        24.5,
        56.8,
        IsCustom: true);
}
