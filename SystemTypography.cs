using System.Drawing.Text;
namespace LegionBar;

internal static class SystemTypography
{
    static readonly string Family = ResolveFamily();
    static string ResolveFamily()
    {
        using var fonts = new InstalledFontCollection();
        var names = fonts.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "Segoe UI Variable Text", "Segoe UI Variable", "Segoe UI" }) if (names.Contains(name)) return name;
        return SystemFonts.MessageBoxFont?.FontFamily.Name ?? FontFamily.GenericSansSerif.Name;
    }
    public static Font Create(float size) => new(Family, size, FontStyle.Regular, GraphicsUnit.Point);
}
