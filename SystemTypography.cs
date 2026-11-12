namespace LegionBar;

internal static class SystemTypography
{
    static readonly string Family = ResolveFamily();
    static string ResolveFamily()
    {
        foreach (var name in new[] { "Segoe UI Variable Text", "Segoe UI Variable", "Segoe UI" })
        {
            try { using var family = new FontFamily(name); if (family.IsStyleAvailable(FontStyle.Regular)) return family.Name; }
            catch (ArgumentException) { }
        }
        return SystemFonts.MessageBoxFont?.FontFamily.Name ?? FontFamily.GenericSansSerif.Name;
    }
    public static Font Create(float size) => new(Family, size, FontStyle.Regular, GraphicsUnit.Point);
}
