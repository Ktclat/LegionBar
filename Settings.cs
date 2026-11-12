using System.Text.Json;
using Microsoft.Win32;
namespace LegionBar;
internal sealed class Settings
{
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegionBar");
    static string FilePath => Path.Combine(DirectoryPath, "settings.json");
    public bool Cpu { get; set; } = true;
    public bool Gpu { get; set; } = true;
    public bool Memory { get; set; } = true;
    public bool Network { get; set; } = true;
    public bool Temperatures { get; set; } = true;
    public bool Compact { get; set; } = true;
    public bool Locked { get; set; } = true;
    public int Interval { get; set; } = 1000;
    public int Offset { get; set; } = 12;
    public int FontSize { get; set; } = 9;
    public string Theme { get; set; } = "auto";
    public string NetworkId { get; set; } = "";
    public string FanSource { get; set; } = "cpu";
    public string DisplayMode { get; set; } = "primary";
    public List<string> SelectedDisplays { get; set; } = [];
    public Dictionary<string, int> DisplayOffsets { get; set; } = new();
    public static Settings Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new();
            s.Interval = Math.Clamp(s.Interval, 1000, 5000); s.FontSize = Math.Clamp(s.FontSize, 8, 12);
            s.SelectedDisplays ??= []; s.DisplayOffsets ??= new();
            if (!(s.Cpu || s.Gpu || s.Memory || s.Network)) s.Cpu = true;
            return s;
        }
        catch { return new(); }
    }
    public void Save()
    {
        try { Directory.CreateDirectory(DirectoryPath); var temp = FilePath + ".tmp"; File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); File.Move(temp, FilePath, true); }
        catch (Exception e) { Program.Log(e); }
    }
    public static bool Startup
    {
        get { using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); return k?.GetValue("LegionBar") != null; }
        set { using var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); if (value) k.SetValue("LegionBar", $"\"{Environment.ProcessPath}\""); else k.DeleteValue("LegionBar", false); }
    }
}
