using System.IO;
using UnityProjectAnalyzer.Models;

namespace UnityProjectAnalyzer.Services;

/// <summary>
/// Unity Editor.log 파서. %LOCALAPPDATA%\Unity\Editor\Editor.log 에서 마지막 N줄을 읽고
/// 에러/경고/정보로 분류한다.
/// </summary>
public class BuildLogService
{
    public static string DefaultLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Unity", "Editor", "Editor.log");

    public static string PrevLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Unity", "Editor", "Editor-prev.log");

    public bool LogExists => File.Exists(DefaultLogPath);

    /// <summary>
    /// Editor.log 의 마지막 tailLines 줄을 읽어 분류
    /// </summary>
    public (List<BuildLogEntry> entries, string sourcePath, DateTime? lastWrite) ReadLatest(int tailLines = 800)
    {
        var path = DefaultLogPath;
        if (!File.Exists(path)) path = PrevLogPath;
        if (!File.Exists(path)) return (new List<BuildLogEntry>(), "", null);

        try
        {
            // 파일 잠금 회피 — 공유 읽기로 오픈
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            var all = sr.ReadToEnd();
            var lines = all.Split('\n');

            int start = Math.Max(0, lines.Length - tailLines);
            var entries = new List<BuildLogEntry>(lines.Length - start);

            for (int i = start; i < lines.Length; i++)
            {
                var raw = lines[i].TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(raw)) continue;

                string sev = "INFO";
                if (raw.Contains("error CS", StringComparison.OrdinalIgnoreCase)
                    || raw.Contains("Exception", StringComparison.Ordinal)
                    || raw.StartsWith("Error", StringComparison.OrdinalIgnoreCase)
                    || raw.Contains(" error ", StringComparison.OrdinalIgnoreCase))
                    sev = "ERR";
                else if (raw.Contains("warning CS", StringComparison.OrdinalIgnoreCase)
                    || raw.StartsWith("Warning", StringComparison.OrdinalIgnoreCase)
                    || raw.Contains("WARNING", StringComparison.Ordinal))
                    sev = "WARN";

                entries.Add(new BuildLogEntry
                {
                    Severity = sev,
                    Message = raw.Length > 500 ? raw.Substring(0, 500) + " …" : raw,
                    LineNumber = i + 1
                });
            }

            return (entries, path, File.GetLastWriteTime(path));
        }
        catch
        {
            return (new List<BuildLogEntry>(), path, null);
        }
    }
}
