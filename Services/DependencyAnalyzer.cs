using System.IO;
using System.Text.RegularExpressions;
using UnityProjectAnalyzer.Models;

namespace UnityProjectAnalyzer.Services;

/// <summary>
/// .meta 파일의 GUID를 인덱싱하고, prefab/scene/material 등 YAML 에셋이 참조하는 GUID를
/// 교차분석해 outgoing/incoming 참조 그래프를 구성한다.
/// 미사용 판정은 Scene/Resources/StreamingAssets/Editor Default Resources를 루트로 BFS.
/// </summary>
public class DependencyAnalyzer
{
    // YAML 안에서 "guid: <32hex>" 추출 — 대소문자 혼용 허용
    private static readonly Regex GuidRefRegex = new(
        @"guid:\s*([0-9a-fA-F]{32})", RegexOptions.Compiled);

    // .meta 파일의 자신의 guid 라인
    private static readonly Regex MetaGuidRegex = new(
        @"^guid:\s*([0-9a-fA-F]{32})", RegexOptions.Compiled | RegexOptions.Multiline);

    // 참조를 스캔할 YAML 텍스트 에셋 확장자
    private static readonly HashSet<string> ScannableExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".unity", ".prefab", ".asset", ".mat", ".controller", ".anim",
        ".overrideController", ".shadergraph", ".vfx", ".spriteatlas",
        ".lighting", ".preset", ".playable", ".inputactions", ".guiskin",
        ".mixer", ".physicMaterial", ".physicsMaterial2D", ".cubemap",
        ".flare", ".fontsettings", ".terrainlayer"
    };

    // 미사용 판정에서 항상 "쓰임"으로 취급할 파일 (코드/에디터 설정 등)
    private static readonly HashSet<string> AlwaysReachableExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".asmdef", ".asmref", ".rsp", ".dll", ".unitypackage", ".pdb"
    };

    public DependencyGraph Build(string assetsPath, List<AssetInfo> assets, IProgress<string>? progress = null)
    {
        var graph = new DependencyGraph();
        if (!Directory.Exists(assetsPath)) return graph;

        progress?.Report("GUID 인덱싱 중...");

        // 1) .meta → guid → 에셋 경로 (역방향: 에셋 경로 → guid)
        var guidToPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pathToGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var metaFiles = Directory.GetFiles(assetsPath, "*.meta", SearchOption.AllDirectories);
        foreach (var meta in metaFiles)
        {
            try
            {
                // .meta 파일은 보통 작아서 전체 읽어도 무방
                var text = File.ReadAllText(meta);
                var m = MetaGuidRegex.Match(text);
                if (!m.Success) continue;

                var guid = m.Groups[1].Value.ToLowerInvariant();
                var assetPath = meta[..^5]; // ".meta" 제거
                if (!File.Exists(assetPath) && !Directory.Exists(assetPath))
                    continue;

                guidToPath[guid] = assetPath;
                pathToGuid[assetPath] = guid;
            }
            catch { /* 깨진 meta는 무시 */ }
        }

        graph.GuidCount = guidToPath.Count;
        progress?.Report($"{guidToPath.Count}개 GUID 발견, 참조 스캔 중...");

        // 2) 스캔 가능한 에셋의 본문에서 guid 참조 추출
        var outgoing = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var incoming = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var asset in assets)
        {
            var ext = Path.GetExtension(asset.Path);
            if (!ScannableExt.Contains(ext)) continue;
            if (!pathToGuid.TryGetValue(asset.Path, out var selfGuid)) continue;

            try
            {
                // 너무 큰 YAML(예: 거대 prefab)은 앞부분만 봐도 대부분의 참조가 잡힘
                string text;
                var fi = new FileInfo(asset.Path);
                if (fi.Length > 4 * 1024 * 1024)
                {
                    using var sr = new StreamReader(asset.Path);
                    var buf = new char[4 * 1024 * 1024];
                    var read = sr.Read(buf, 0, buf.Length);
                    text = new string(buf, 0, read);
                }
                else
                {
                    text = File.ReadAllText(asset.Path);
                }

                var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match gm in GuidRefRegex.Matches(text))
                {
                    var refGuid = gm.Groups[1].Value.ToLowerInvariant();
                    if (refGuid == selfGuid) continue;       // 자기 자신 제외
                    if (!guidToPath.ContainsKey(refGuid)) continue; // 외부 GUID(빌트인 등) 무시
                    refs.Add(refGuid);
                }

                if (refs.Count > 0)
                {
                    outgoing[selfGuid] = refs;
                    foreach (var r in refs)
                    {
                        if (!incoming.TryGetValue(r, out var set))
                            incoming[r] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        set.Add(selfGuid);
                    }
                }
            }
            catch { /* 읽기 실패는 무시 */ }
        }

        progress?.Report("미사용 에셋 판정 중...");

        // 3) 루트 선정 → BFS로 reachable 마킹
        var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();

        var assetsRoot = assetsPath.TrimEnd('\\', '/');

        foreach (var (path, guid) in pathToGuid)
        {
            if (IsRoot(path, assetsRoot))
            {
                if (reachable.Add(guid)) queue.Enqueue(guid);
            }
        }

        while (queue.Count > 0)
        {
            var g = queue.Dequeue();
            if (!outgoing.TryGetValue(g, out var refs)) continue;
            foreach (var child in refs)
                if (reachable.Add(child)) queue.Enqueue(child);
        }

        // 4) 노드 빌드 — assets 순서를 그대로 따름
        foreach (var asset in assets)
        {
            if (!pathToGuid.TryGetValue(asset.Path, out var guid)) continue;

            var inSet = incoming.TryGetValue(guid, out var inRefs) ? inRefs : null;
            var outSet = outgoing.TryGetValue(guid, out var outRefs) ? outRefs : null;

            var node = new AssetDependency
            {
                Guid = guid,
                Path = asset.Path,
                Name = asset.Name,
                Category = asset.Category,
                Size = asset.Size,
                IncomingCount = inSet?.Count ?? 0,
                OutgoingCount = outSet?.Count ?? 0,
                IsUnused = !reachable.Contains(guid)
                          && !IsRoot(asset.Path, assetsRoot)
                          && !AlwaysReachableExt.Contains(Path.GetExtension(asset.Path)),
                RelativePath = ToRelative(asset.Path, assetsRoot)
            };

            if (inSet != null)
            {
                foreach (var refGuid in inSet)
                    if (guidToPath.TryGetValue(refGuid, out var p))
                        node.IncomingPaths.Add(ToRelative(p, assetsRoot));
                node.IncomingPaths.Sort(StringComparer.OrdinalIgnoreCase);
            }
            if (outSet != null)
            {
                foreach (var refGuid in outSet)
                    if (guidToPath.TryGetValue(refGuid, out var p))
                        node.OutgoingPaths.Add(ToRelative(p, assetsRoot));
                node.OutgoingPaths.Sort(StringComparer.OrdinalIgnoreCase);
            }

            graph.Nodes.Add(node);
        }

        graph.UnusedCount = graph.Nodes.Count(n => n.IsUnused);
        graph.UnusedSize = graph.Nodes.Where(n => n.IsUnused).Sum(n => n.Size);
        graph.ScannedRefCount = outgoing.Values.Sum(v => v.Count);

        return graph;
    }

    /// <summary>
    /// Resources / StreamingAssets / Editor Default Resources 안에 있거나
    /// 씬(.unity)이거나 코드/asmdef 인 경우 루트 (Unity가 직접 로드할 수 있음)
    /// </summary>
    private static bool IsRoot(string fullPath, string assetsRoot)
    {
        var ext = Path.GetExtension(fullPath);
        if (string.Equals(ext, ".unity", StringComparison.OrdinalIgnoreCase)) return true;
        if (AlwaysReachableExt.Contains(ext)) return true;

        var rel = ToRelative(fullPath, assetsRoot).Replace('\\', '/');
        if (rel.Contains("/Resources/", StringComparison.OrdinalIgnoreCase)) return true;
        if (rel.Contains("/StreamingAssets/", StringComparison.OrdinalIgnoreCase)) return true;
        if (rel.Contains("/Editor Default Resources/", StringComparison.OrdinalIgnoreCase)) return true;
        if (rel.StartsWith("Resources/", StringComparison.OrdinalIgnoreCase)) return true;
        if (rel.StartsWith("StreamingAssets/", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static string ToRelative(string fullPath, string assetsRoot)
    {
        if (fullPath.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
            return "Assets" + fullPath[assetsRoot.Length..].Replace('\\', '/');
        return fullPath.Replace('\\', '/');
    }
}
