using System.IO;
using System.Text.RegularExpressions;
using UnityProjectAnalyzer.Models;

namespace UnityProjectAnalyzer.Services;

/// <summary>
/// Unity .cs 파일을 정규식/문자열 기반으로 빠르게 훑어 휴리스틱 진단을 만든다.
/// Roslyn 없이 동작 — 가짜양성 일부 허용하는 대신 즉시성/오프라인 우선.
/// </summary>
public class CodeAnalyzer
{
    private static readonly Regex ClassRegex = new(
        @"\bclass\s+(\w+)\s*(?::\s*([^\{]+))?", RegexOptions.Compiled);
    private static readonly Regex PublicFieldRegex = new(
        @"^\s*public\s+(?!class|struct|enum|interface|delegate|event|static\s+class|abstract|sealed|partial|override|virtual|async)([A-Za-z_<][\w<>,\[\]\s\.\?]*?)\s+\w+\s*(?:=|;)",
        RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex AsyncVoidRegex = new(
        @"\basync\s+void\s+(\w+)\s*\(", RegexOptions.Compiled);
    private static readonly Regex EmptyCatchRegex = new(
        @"catch\s*(?:\([^)]*\))?\s*\{\s*\}", RegexOptions.Compiled);
    private static readonly Regex UpdateBodyRegex = new(
        @"\b(?:void|IEnumerator)\s+(Update|FixedUpdate|LateUpdate)\s*\(\s*\)\s*\{",
        RegexOptions.Compiled);
    private static readonly Regex DebugLogRegex = new(
        @"\bDebug\.Log(?:Warning|Error)?\s*\(", RegexOptions.Compiled);
    private static readonly Regex MagicStringRegex = new(
        @"GameObject\.Find\s*\(\s*""", RegexOptions.Compiled);

    private const int BigFileLineThreshold = 500;
    private const int HugeFileLineThreshold = 1000;
    private const int MaxFileBytesForAi = 64 * 1024; // 64KB 넘으면 AI에 안 보냄(잘림)

    public List<ScriptFile> Scan(string assetsPath, List<IssueItem> issuesAccumulator)
    {
        var result = new List<ScriptFile>();
        if (!Directory.Exists(assetsPath)) return result;

        var csFiles = Directory.GetFiles(assetsPath, "*.cs", SearchOption.AllDirectories);

        // 1차 패스: 파일별 분석
        var monoNames = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var file in csFiles)
        {
            ScriptFile? sf = null;
            try { sf = AnalyzeFile(file, assetsPath, monoNames); }
            catch { /* 깨진 파일은 그냥 무시 */ }
            if (sf != null) result.Add(sf);
        }

        // 2차 패스: 중복 MonoBehaviour 클래스 이름
        foreach (var (className, paths) in monoNames)
        {
            if (paths.Count > 1)
            {
                issuesAccumulator.Add(new IssueItem
                {
                    Severity = "WARN",
                    Message = $"MonoBehaviour 클래스 이름 중복: {className} ({paths.Count}곳)",
                    Source = "코드 분석",
                    TimeAgo = "방금",
                    HasAiSolution = true
                });
            }
        }

        // 종합 통계 → Issues에 반영
        var bigCount = result.Count(s => s.LineCount > BigFileLineThreshold);
        if (bigCount > 0)
        {
            issuesAccumulator.Add(new IssueItem
            {
                Severity = "INFO",
                Message = $"{BigFileLineThreshold}줄 초과 스크립트 {bigCount}개 — 분리/추출 검토",
                Source = "코드 분석",
                TimeAgo = "방금",
                HasAiSolution = true
            });
        }
        var updateHotCount = result.Count(s => s.Findings.Any(f => f.StartsWith("Update에 비싼 호출")));
        if (updateHotCount > 0)
        {
            issuesAccumulator.Add(new IssueItem
            {
                Severity = "WARN",
                Message = $"Update 루프 안에서 비싼 호출(GetComponent/Find) 사용 {updateHotCount}개",
                Source = "코드 분석",
                TimeAgo = "방금",
                HasAiSolution = true
            });
        }
        var asyncVoidCount = result.Count(s => s.Findings.Any(f => f.Contains("async void")));
        if (asyncVoidCount > 0)
        {
            issuesAccumulator.Add(new IssueItem
            {
                Severity = "WARN",
                Message = $"async void 메서드 발견 {asyncVoidCount}개 — UniTask 또는 async Task 권장",
                Source = "코드 분석",
                TimeAgo = "방금",
                HasAiSolution = true
            });
        }

        return result
            .OrderByDescending(s => s.FindingCount)
            .ThenByDescending(s => s.LineCount)
            .ToList();
    }

    private ScriptFile? AnalyzeFile(string filePath, string assetsPath, Dictionary<string, List<string>> monoNames)
    {
        var info = new FileInfo(filePath);
        if (info.Length == 0) return null;

        // 너무 큰 파일은 헤더만 빠르게 보고 라인 카운트
        var content = info.Length > 1_000_000
            ? File.ReadAllText(filePath)[..1_000_000]
            : File.ReadAllText(filePath);

        var lineCount = CountLines(content);
        var rel = filePath.StartsWith(assetsPath, StringComparison.OrdinalIgnoreCase)
            ? "Assets" + filePath[assetsPath.Length..].Replace('\\', '/')
            : Path.GetFileName(filePath);

        var sf = new ScriptFile
        {
            Path = filePath,
            Name = Path.GetFileNameWithoutExtension(filePath),
            RelativePath = rel,
            LineCount = lineCount,
            Size = info.Length,
            IsEditor = rel.Contains("/Editor/", StringComparison.OrdinalIgnoreCase),
        };

        // 클래스 / 상속 식별
        foreach (Match m in ClassRegex.Matches(content))
        {
            var className = m.Groups[1].Value;
            var bases = m.Groups[2].Success ? m.Groups[2].Value : "";
            if (bases.Contains("MonoBehaviour"))
            {
                sf.IsMonoBehaviour = true;
                if (!monoNames.TryGetValue(className, out var list))
                    monoNames[className] = list = new List<string>();
                list.Add(filePath);
            }
            if (bases.Contains("ScriptableObject")) sf.IsScriptableObject = true;
        }

        // 1) 큰 파일
        if (lineCount > HugeFileLineThreshold)
            sf.Findings.Add($"거대 파일({lineCount}줄)");
        else if (lineCount > BigFileLineThreshold)
            sf.Findings.Add($"큰 파일({lineCount}줄)");

        // 2) Update 류 안에서 비싼 호출
        foreach (Match m in UpdateBodyRegex.Matches(content))
        {
            var bodyStart = m.Index + m.Length;
            var body = ExtractMethodBody(content, bodyStart);
            if (body == null) continue;
            var hot = new List<string>();
            if (body.Contains("GetComponent")) hot.Add("GetComponent");
            if (body.Contains("FindObjectOfType") || body.Contains("FindAnyObjectByType") || body.Contains("FindFirstObjectByType"))
                hot.Add("FindObjectOfType");
            if (body.Contains("GameObject.Find")) hot.Add("GameObject.Find");
            if (body.Contains("new ") && Regex.IsMatch(body, @"new\s+(Vector3|Vector2|Quaternion|List<|Dictionary<)"))
                hot.Add("매 프레임 객체 할당");
            if (hot.Count > 0)
                sf.Findings.Add($"Update에 비싼 호출: {string.Join(", ", hot)}");
        }

        // 3) public 필드 (Inspector 노출이라면 SerializeField + private이 안전)
        var publicFieldCount = PublicFieldRegex.Matches(content).Count;
        if (publicFieldCount > 3)
            sf.Findings.Add($"public 필드 {publicFieldCount}개 — [SerializeField] private 권장");

        // 4) async void
        var asyncVoid = AsyncVoidRegex.Matches(content).Count;
        if (asyncVoid > 0)
            sf.Findings.Add($"async void 메서드 {asyncVoid}개");

        // 5) 비어있는 catch
        var emptyCatch = EmptyCatchRegex.Matches(content).Count;
        if (emptyCatch > 0)
            sf.Findings.Add($"비어있는 catch {emptyCatch}곳 — 예외 삼킴");

        // 6) Debug.Log 너무 많음 (출시 시 성능/노이즈)
        var logCount = DebugLogRegex.Matches(content).Count;
        if (logCount > 15)
            sf.Findings.Add($"Debug.Log {logCount}개 — 출시 빌드에서 정리 권장");

        // 7) Find("string") 매직 스트링
        var findCount = MagicStringRegex.Matches(content).Count;
        if (findCount > 0)
            sf.Findings.Add($"GameObject.Find(\"...\") {findCount}곳 — 참조 직접 연결 권장");

        return sf;
    }

    private static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        int count = 1;
        foreach (var c in text) if (c == '\n') count++;
        return count;
    }

    /// <summary>
    /// '{' 직후 인덱스에서 시작해 짝 맞는 '}'까지의 본문을 반환.
    /// 문자열/주석 안 무시(가짜양성 허용)
    /// </summary>
    private static string? ExtractMethodBody(string source, int openBraceAfter)
    {
        int depth = 1;
        int start = openBraceAfter;
        for (int i = start; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return source[start..i];
            }
        }
        return null;
    }

    public static int MaxAiBytes => MaxFileBytesForAi;
}
