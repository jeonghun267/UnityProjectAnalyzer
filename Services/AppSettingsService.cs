using System.IO;
using Newtonsoft.Json;

namespace UnityProjectAnalyzer.Services;

public class AppSettings
{
    public string GeminiApiKey { get; set; } = "";
    public string GeminiModel { get; set; } = "gemini-2.5-flash";
    public string GithubRepo { get; set; } = "";
    public string LastProjectPath { get; set; } = "";
}

public static class AppSettingsService
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "UnityProjectAnalyzer");
    private static readonly string File = Path.Combine(Dir, "config.json");

    public static AppSettings Load()
    {
        try
        {
            if (!System.IO.File.Exists(File)) return new AppSettings();
            var json = System.IO.File.ReadAllText(File);
            return JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            System.IO.File.WriteAllText(File, json);
        }
        catch { }
    }
}
