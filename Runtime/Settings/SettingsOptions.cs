namespace Moirai.Atropos.Settings
{
    public sealed class SettingsOptions
    {
        public string FilePath { get; set; } = "settings.v2.json";
        public int FormatVersion { get; set; } = 2;
    }
}
