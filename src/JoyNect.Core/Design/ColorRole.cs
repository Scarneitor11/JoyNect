namespace JoyNect.Core.Design;

/// <summary>The colour roles of the Material 3 scheme.</summary>
/// <remarks>
/// The order is part of the contract with the fixture file used to verify the colour system against
/// the reference implementation, and <see cref="ColorRoleInfo"/> carries the reference names.
/// </remarks>
public enum ColorRole
{
    Primary,
    OnPrimary,
    PrimaryContainer,
    OnPrimaryContainer,
    PrimaryFixed,
    PrimaryFixedDim,
    OnPrimaryFixed,
    OnPrimaryFixedVariant,

    Secondary,
    OnSecondary,
    SecondaryContainer,
    OnSecondaryContainer,
    SecondaryFixed,
    SecondaryFixedDim,
    OnSecondaryFixed,
    OnSecondaryFixedVariant,

    Tertiary,
    OnTertiary,
    TertiaryContainer,
    OnTertiaryContainer,
    TertiaryFixed,
    TertiaryFixedDim,
    OnTertiaryFixed,
    OnTertiaryFixedVariant,

    Error,
    OnError,
    ErrorContainer,
    OnErrorContainer,
    ErrorFixed,
    ErrorFixedDim,
    OnErrorFixed,
    OnErrorFixedVariant,

    Surface,
    OnSurface,
    SurfaceVariant,
    OnSurfaceVariant,
    SurfaceDim,
    SurfaceBright,
    SurfaceContainerLowest,
    SurfaceContainerLow,
    SurfaceContainer,
    SurfaceContainerHigh,
    SurfaceContainerHighest,

    InverseSurface,
    InverseOnSurface,
    InversePrimary,

    Outline,
    OutlineVariant,
    Shadow,
    Scrim,
    SurfaceTint,
    Background,
    OnBackground,
}

/// <summary>One colour role with the names it is known by.</summary>
public sealed record ColorRoleInfo(ColorRole Role, string Key, string DisplayName);

/// <summary>Names and descriptions for every role.</summary>
public static class ColorRoleInfoTable
{
    /// <summary>All roles, in declaration order.</summary>
    public static IReadOnlyList<ColorRoleInfo> All { get; } =
    [
        new(ColorRole.Primary, "primary", "Primary"),
        new(ColorRole.OnPrimary, "onPrimary", "On primary"),
        new(ColorRole.PrimaryContainer, "primaryContainer", "Primary container"),
        new(ColorRole.OnPrimaryContainer, "onPrimaryContainer", "On primary container"),
        new(ColorRole.PrimaryFixed, "primaryFixed", "Primary fixed"),
        new(ColorRole.PrimaryFixedDim, "primaryFixedDim", "Primary fixed dim"),
        new(ColorRole.OnPrimaryFixed, "onPrimaryFixed", "On primary fixed"),
        new(ColorRole.OnPrimaryFixedVariant, "onPrimaryFixedVariant", "On primary fixed variant"),

        new(ColorRole.Secondary, "secondary", "Secondary"),
        new(ColorRole.OnSecondary, "onSecondary", "On secondary"),
        new(ColorRole.SecondaryContainer, "secondaryContainer", "Secondary container"),
        new(ColorRole.OnSecondaryContainer, "onSecondaryContainer", "On secondary container"),
        new(ColorRole.SecondaryFixed, "secondaryFixed", "Secondary fixed"),
        new(ColorRole.SecondaryFixedDim, "secondaryFixedDim", "Secondary fixed dim"),
        new(ColorRole.OnSecondaryFixed, "onSecondaryFixed", "On secondary fixed"),
        new(ColorRole.OnSecondaryFixedVariant, "onSecondaryFixedVariant", "On secondary fixed variant"),

        new(ColorRole.Tertiary, "tertiary", "Tertiary"),
        new(ColorRole.OnTertiary, "onTertiary", "On tertiary"),
        new(ColorRole.TertiaryContainer, "tertiaryContainer", "Tertiary container"),
        new(ColorRole.OnTertiaryContainer, "onTertiaryContainer", "On tertiary container"),
        new(ColorRole.TertiaryFixed, "tertiaryFixed", "Tertiary fixed"),
        new(ColorRole.TertiaryFixedDim, "tertiaryFixedDim", "Tertiary fixed dim"),
        new(ColorRole.OnTertiaryFixed, "onTertiaryFixed", "On tertiary fixed"),
        new(ColorRole.OnTertiaryFixedVariant, "onTertiaryFixedVariant", "On tertiary fixed variant"),

        new(ColorRole.Error, "error", "Error"),
        new(ColorRole.OnError, "onError", "On error"),
        new(ColorRole.ErrorContainer, "errorContainer", "Error container"),
        new(ColorRole.OnErrorContainer, "onErrorContainer", "On error container"),
        new(ColorRole.ErrorFixed, "errorFixed", "Error fixed"),
        new(ColorRole.ErrorFixedDim, "errorFixedDim", "Error fixed dim"),
        new(ColorRole.OnErrorFixed, "onErrorFixed", "On error fixed"),
        new(ColorRole.OnErrorFixedVariant, "onErrorFixedVariant", "On error fixed variant"),

        new(ColorRole.Surface, "surface", "Surface"),
        new(ColorRole.OnSurface, "onSurface", "On surface"),
        new(ColorRole.SurfaceVariant, "surfaceVariant", "Surface variant"),
        new(ColorRole.OnSurfaceVariant, "onSurfaceVariant", "On surface variant"),
        new(ColorRole.SurfaceDim, "surfaceDim", "Surface dim"),
        new(ColorRole.SurfaceBright, "surfaceBright", "Surface bright"),
        new(ColorRole.SurfaceContainerLowest, "surfaceContainerLowest", "Surface container lowest"),
        new(ColorRole.SurfaceContainerLow, "surfaceContainerLow", "Surface container low"),
        new(ColorRole.SurfaceContainer, "surfaceContainer", "Surface container"),
        new(ColorRole.SurfaceContainerHigh, "surfaceContainerHigh", "Surface container high"),
        new(ColorRole.SurfaceContainerHighest, "surfaceContainerHighest", "Surface container highest"),

        new(ColorRole.InverseSurface, "inverseSurface", "Inverse surface"),
        new(ColorRole.InverseOnSurface, "inverseOnSurface", "Inverse on surface"),
        new(ColorRole.InversePrimary, "inversePrimary", "Inverse primary"),

        new(ColorRole.Outline, "outline", "Outline"),
        new(ColorRole.OutlineVariant, "outlineVariant", "Outline variant"),
        new(ColorRole.Shadow, "shadow", "Shadow"),
        new(ColorRole.Scrim, "scrim", "Scrim"),
        new(ColorRole.SurfaceTint, "surfaceTint", "Surface tint"),
        new(ColorRole.Background, "background", "Background"),
        new(ColorRole.OnBackground, "onBackground", "On background"),
    ];

    private static readonly Dictionary<string, ColorRole> ByKey =
        All.ToDictionary(info => info.Key, info => info.Role, StringComparer.Ordinal);

    /// <summary>Finds a role from the reference name, e.g. <c>surfaceContainerHigh</c>.</summary>
    public static ColorRole? FromKey(string key) => ByKey.TryGetValue(key, out var role) ? role : null;

    /// <summary>The display name of a role.</summary>
    public static string NameOf(ColorRole role) =>
        All[(int)role].DisplayName;
}
