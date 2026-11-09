namespace DiscImageStudio.Dvd;

public sealed record DvdDiscPreset(
    string Id,
    string DisplayName,
    string Description,
    uint TotalSectors,
    double InnerRadiusMm,
    double OuterRadiusMm,
    string? DisplayNameResourceKey = null,
    string? DescriptionResourceKey = null,
    bool IsCustom = false,
    double PitchLinear = 0,
    double PitchQuadratic = 0,
    double PitchCubic = 0,
    string? Contributor = null)
{
    public static DvdDiscPreset Manual { get; } = new(
        "__manual__",
        "Custom parameters (unsaved)",
        "Generation parameters have been adjusted manually. Save them as a user preset to keep them.",
        2_295_104,
        24.0,
        58.0,
        IsCustom: true);
}
