using System.IO;
using UnityProjectAnalyzer.Models;

namespace UnityProjectAnalyzer.Services;

public class UnityProjectAnalyzer
{
    public class AnalysisResult
    {
        public string ProjectPath { get; set; } = "";
        public string UnityVersion { get; set; } = "Unknown";
        public int TotalAssetCount { get; set; }
        public long TotalSize { get; set; }
        public int UnusedAssetCount { get; set; }
        public long UnusedSize { get; set; }
        public List<AssetInfo> Assets { get; set; } = new();
        public List<CategoryStat> Categories { get; set; } = new();
        public List<CategoryStat> TopFolders { get; set; } = new();
        public List<IssueItem> Issues { get; set; } = new();
        public List<ScriptFile> Scripts { get; set; } = new();
        public DependencyGraph Dependencies { get; set; } = new();
        public List<ImportFinding> ImportFindings { get; set; } = new();
    }

    private readonly CodeAnalyzer _codeAnalyzer = new();
    private readonly DependencyAnalyzer _dependencyAnalyzer = new();
    private readonly MetaImporterAnalyzer _metaAnalyzer = new();

    /// <summary>
    /// 폴더가 Unity 프로젝트인지 확인
    /// </summary>
    public bool IsUnityProject(string path)
    {
        return Directory.Exists(Path.Combine(path, "Assets")) &&
               Directory.Exists(Path.Combine(path, "ProjectSettings"    ));
    }

    /// <summary>
    /// 메인 분석 함수
    /// </summary>
    public async Task<AnalysisResult> AnalyzeAsync(string projectPath, IProgress<string>? progress = null)
    {
        return await Task.Run(() =>
        {
            var result = new AnalysisResult { ProjectPath = projectPath };

            progress?.Report("Unity 버전 확인 중...");
            result.UnityVersion = ReadUnityVersion(projectPath);

            progress?.Report("Assets 폴더 스캔 중...");
            var assetsPath = Path.Combine(projectPath, "Assets");
            var allFiles = Directory.GetFiles(assetsPath, "*.*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".meta"))
                .ToList();

            progress?.Report($"{allFiles.Count}개 파일 분석 중...");

            foreach (var file in allFiles)
            {
                var info = new FileInfo(file);
                var asset = new AssetInfo
                {
                    Path = file,
                    Name = info.Name,
                    Extension = info.Extension.ToLower(),
                    Size = info.Length,
                    Category = CategorizeAsset(info.Extension)
                };
                result.Assets.Add(asset);
            }

            result.TotalAssetCount = result.Assets.Count;
            result.TotalSize = result.Assets.Sum(a => a.Size);

            // 카테고리별 집계
            progress?.Report("카테고리 분류 중...");
            result.Categories = result.Assets
                .GroupBy(a => a.Category)
                .Select(g => new CategoryStat
                {
                    Name = g.Key,
                    Count = g.Count(),
                    TotalSize = g.Sum(a => a.Size)
                })
                .OrderByDescending(c => c.TotalSize)
                .ToList();

            // 폴더별 Top 5 (상대 비율 포함)
            progress?.Report("폴더 분석 중...");
            result.TopFolders = result.Assets
                .GroupBy(a => GetTopFolder(a.Path, assetsPath))
                .Select(g => new CategoryStat
                {
                    Name = g.Key,
                    Count = g.Count(),
                    TotalSize = g.Sum(a => a.Size)
                })
                .OrderByDescending(c => c.TotalSize)
                .Take(5)
                .ToList();

            if (result.TopFolders.Count > 0)
            {
                var max = (double)result.TopFolders[0].TotalSize;
                foreach (var f in result.TopFolders)
                    f.Percent = max > 0 ? f.TotalSize / max : 0;
            }

            // GUID 의존성 분석 (정확한 미사용 판정)
            result.Dependencies = _dependencyAnalyzer.Build(assetsPath, result.Assets, progress);
            result.UnusedAssetCount = result.Dependencies.UnusedCount;
            result.UnusedSize = result.Dependencies.UnusedSize;

            // .meta 임포터 설정 분석
            progress?.Report("에셋 임포트 설정 검사 중...");
            result.ImportFindings = _metaAnalyzer.Analyze(result.Assets, assetsPath, progress);

            // 이슈 분석
            progress?.Report("이슈 분석 중...");
            result.Issues = DetectIssues(result, projectPath);
            AggregateImportIssues(result.ImportFindings, result.Issues);

            // 코드 분석 (스크립트 휴리스틱)
            progress?.Report("스크립트 코드 분석 중...");
            result.Scripts = _codeAnalyzer.Scan(assetsPath, result.Issues);

            progress?.Report("분석 완료!");
            return result;
        });
    }

    private string ReadUnityVersion(string projectPath)
    {
        try
        {
            var versionFile = Path.Combine(projectPath, "ProjectSettings", "ProjectVersion.txt");
            if (File.Exists(versionFile))
            {
                var lines = File.ReadAllLines(versionFile);
                var verLine = lines.FirstOrDefault(l => l.StartsWith("m_EditorVersion:"));
                if (verLine != null)
                    return verLine.Replace("m_EditorVersion:", "").Trim();
            }
        }
        catch { }
        return "Unknown";
    }

    private string CategorizeAsset(string ext)
    {
        return ext.ToLower() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".tga" or ".psd" or ".tif" or ".exr" => "텍스처",
            ".fbx" or ".obj" or ".blend" or ".dae" or ".3ds" => "3D 모델",
            ".wav" or ".mp3" or ".ogg" or ".aif" or ".aiff" => "오디오",
            ".cs" or ".js" => "스크립트",
            ".prefab" => "프리팹",
            ".unity" => "씬",
            ".mat" => "머티리얼",
            ".anim" or ".controller" => "애니메이션",
            ".shader" or ".shadergraph" => "셰이더",
            _ => "기타"
        };
    }

    private string GetTopFolder(string fullPath, string assetsPath)
    {
        var relative = fullPath.Substring(assetsPath.Length).TrimStart('\\', '/');
        var parts = relative.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] + "/" : "(root)";
    }

    /// <summary>
    /// .meta 임포터 진단 결과를 CheckId 기준으로 집계해 Issues 리스트에 한 줄씩 추가.
    /// </summary>
    private static void AggregateImportIssues(List<ImportFinding> findings, List<IssueItem> issues)
    {
        foreach (var group in findings.GroupBy(f => f.CheckId))
        {
            var sample = group.First();
            var totalSize = group.Sum(g => g.Size);
            issues.Add(new IssueItem
            {
                Severity = sample.Severity,
                Message = $"{sample.Message} — {group.Count()}개 ({CategoryStat.FormatSize(totalSize)})",
                Source = "임포트 설정",
                TimeAgo = "방금",
                HasAiSolution = true
            });
        }
    }

    private List<IssueItem> DetectIssues(AnalysisResult result, string projectPath)
    {
        var issues = new List<IssueItem>();

        // 큰 텍스처 탐지
        var bigTextures = result.Assets
            .Where(a => a.Category == "텍스처" && a.Size > 5 * 1024 * 1024)
            .ToList();
        if (bigTextures.Any())
        {
            issues.Add(new IssueItem
            {
                Severity = "WARN",
                Message = $"5MB 초과 텍스처 {bigTextures.Count}개 발견 — 압축 권장",
                Source = "텍스처 분석",
                TimeAgo = "방금",
                HasAiSolution = true
            });
        }

        // 미사용 에셋
        if (result.UnusedAssetCount > 0)
        {
            issues.Add(new IssueItem
            {
                Severity = "INFO",
                Message = $"{result.UnusedAssetCount}개의 미사용 에셋 추정 — {CategoryStat.FormatSize(result.UnusedSize)} 절감 가능",
                Source = "에셋 분석",
                TimeAgo = "방금",
                HasAiSolution = true
            });
        }

        // Editor.log 분석
        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Unity", "Editor", "Editor.log");

            if (File.Exists(logPath))
            {
                var logContent = File.ReadAllLines(logPath);
                var errors = logContent
                    .Where(l => l.Contains("error CS") || l.Contains("Exception"))
                    .Take(3)
                    .ToList();

                foreach (var err in errors)
                {
                    issues.Add(new IssueItem
                    {
                        Severity = "ERR",
                        Message = err.Length > 100 ? err.Substring(0, 100) + "..." : err,
                        Source = "Editor.log",
                        TimeAgo = "최근",
                        HasAiSolution = true
                    });
                }
            }
        }
        catch { }

        // 총 용량 체크
        if (result.TotalSize > 5L * 1024 * 1024 * 1024)
        {
            issues.Add(new IssueItem
            {
                Severity = "WARN",
                Message = "프로젝트 용량 5GB 초과 — 빌드 시간 영향",
                Source = "프로젝트 분석",
                TimeAgo = "방금",
                HasAiSolution = false
            });
        }

        return issues;
    }
}
