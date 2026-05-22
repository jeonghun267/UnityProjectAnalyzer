using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace UnityProjectAnalyzer.Models;

public enum AppPage { Dashboard, Assets, Issues, Scripts, Dependency, ImportSettings, BuildLog, Git, Settings }

public partial class NavItem : ObservableObject
{
    public string Page { get; set; } = "";       // AppPage enum 이름
    public string Icon { get; set; } = "";
    public string Label { get; set; } = "";
    [ObservableProperty] private string badge = "";
    [ObservableProperty] private string badgeStyle = "danger"; // danger / warn / info
    [ObservableProperty] private bool isActive;
    public bool HasBadge => !string.IsNullOrEmpty(Badge);
    partial void OnBadgeChanged(string value) => OnPropertyChanged(nameof(HasBadge));
}

public class BuildLogEntry
{
    public string Severity { get; set; } = "INFO"; // ERR / WARN / INFO
    public string Message { get; set; } = "";
    public int LineNumber { get; set; }
}

/// <summary>
/// 도넛 차트 1조각. Ellipse + StrokeDashArray + RotateTransform으로 그려짐.
/// 둘레는 stroke width 단위 기준 (StrokeDashArray가 width 배수임에 주의)
/// </summary>
public class DonutSegment
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#4fffb0";
    public Brush Brush => new SolidColorBrush((Color)ColorConverter.ConvertFromString(Color));
    public DoubleCollection DashArray { get; set; } = new();
    public double StartAngle { get; set; } // degrees, 12시=−90 기준
    public double Percent { get; set; }    // 0..1
    public string PercentText => $"{Percent * 100:F1}%";
    public string SizeText { get; set; } = "";
}

public class AssetInfo
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public string Extension { get; set; } = "";
    public long Size { get; set; }
    public string Category { get; set; } = "";
}

public class IssueItem
{
    public string Severity { get; set; } = "INFO";
    public string Message { get; set; } = "";
    public string Source { get; set; } = "";
    public string TimeAgo { get; set; } = "";
    public bool HasAiSolution { get; set; }
}

/// <summary>
/// Unity .cs 스크립트 1개. 휴리스틱 분석 결과 포함
/// </summary>
public class ScriptFile
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public int LineCount { get; set; }
    public long Size { get; set; }
    public bool IsMonoBehaviour { get; set; }
    public bool IsScriptableObject { get; set; }
    public bool IsEditor { get; set; }
    public List<string> Findings { get; set; } = new();
    public int FindingCount => Findings.Count;
    public bool HasFindings => Findings.Count > 0;
    public string Severity => Findings.Count == 0 ? "OK"
        : Findings.Count >= 4 ? "ERR"
        : Findings.Count >= 2 ? "WARN" : "INFO";
    public string SubtitleText
    {
        get
        {
            var kind = IsEditor ? "Editor" : IsMonoBehaviour ? "MonoBehaviour" : IsScriptableObject ? "ScriptableObject" : "Class";
            return $"{kind} · {LineCount} lines";
        }
    }
    public string FindingsSummary => Findings.Count == 0 ? "특이사항 없음" : string.Join(" · ", Findings);
}

public class GitCommit
{
    public string Sha { get; set; } = "";
    public string ShortSha => Sha.Length > 6 ? Sha.Substring(0, 6) : Sha;
    public string Message { get; set; } = "";
    public string Author { get; set; } = "";
    public string Date { get; set; } = "";
    public string TimeAgo { get; set; } = "";
    public string Url { get; set; } = "";
}

public class ChatMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
    public bool IsAi => Role == "assistant";
}

public class CategoryStat
{
    public string Name { get; set; } = "";
    public long TotalSize { get; set; }
    public int Count { get; set; }
    public double Percent { get; set; } = 1.0; // 0.0 ~ 1.0 (상대 비율, 차트 막대 길이)
    public string SizeText => FormatSize(TotalSize);

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024:F1} MB";
        return $"{bytes / 1024.0 / 1024 / 1024:F2} GB";
    }
}

/// <summary>
/// GUID 기반 의존성 그래프의 노드. UI에서는 incoming/outgoing 경로 목록을 트리뷰로 펼친다.
/// </summary>
public class AssetDependency
{
    public string Guid { get; set; } = "";
    public string Path { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public long Size { get; set; }
    public int IncomingCount { get; set; }
    public int OutgoingCount { get; set; }
    public bool IsUnused { get; set; }
    public List<string> IncomingPaths { get; } = new();
    public List<string> OutgoingPaths { get; } = new();

    public string SizeText => CategoryStat.FormatSize(Size);
    public string Subtitle => $"{Category} · {SizeText} · {(IsUnused ? "미사용" : $"In {IncomingCount} / Out {OutgoingCount}")}";
    public string Severity => IsUnused ? "WARN" : IncomingCount == 0 && OutgoingCount > 0 ? "INFO" : "OK";
}

public class DependencyGraph
{
    public List<AssetDependency> Nodes { get; } = new();
    public int GuidCount { get; set; }
    public int ScannedRefCount { get; set; }
    public int UnusedCount { get; set; }
    public long UnusedSize { get; set; }
}

/// <summary>
/// .meta 파일을 읽어 텍스처/오디오/모델 임포터 설정에서 발견한 문제 1건.
/// </summary>
public class ImportFinding
{
    public string AssetPath { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string Name { get; set; } = "";
    public string AssetKind { get; set; } = "";   // 텍스처 / 오디오 / 3D 모델
    public string Severity { get; set; } = "INFO"; // ERR / WARN / INFO
    public string CheckId { get; set; } = "";      // 그룹핑/집계 키
    public string Message { get; set; } = "";
    public string Detail { get; set; } = "";       // 발견된 값 (예: "MaxSize=8192, Compressed=No")
    public long Size { get; set; }
    public string SizeText => CategoryStat.FormatSize(Size);
}
