using System.IO;
using System.Text.RegularExpressions;
using UnityProjectAnalyzer.Models;

namespace UnityProjectAnalyzer.Services;

/// <summary>
/// 에셋의 .meta YAML을 정규식 기반으로 빠르게 훑어 Unity 임포터 설정의 잘못된 값을 진단.
/// 텍스처(R/W·MaxSize·압축·Mipmap) / 오디오(LoadType·preload) / 모델(R/W) 검사 지원.
/// </summary>
public class MetaImporterAnalyzer
{
    // 텍스처 카테고리 확장자
    private static readonly HashSet<string> TextureExt = new(StringComparer.OrdinalIgnoreCase)
    { ".png", ".jpg", ".jpeg", ".tga", ".psd", ".tif", ".tiff", ".exr", ".bmp", ".gif" };

    // 오디오 카테고리 확장자
    private static readonly HashSet<string> AudioExt = new(StringComparer.OrdinalIgnoreCase)
    { ".wav", ".mp3", ".ogg", ".aif", ".aiff", ".flac", ".m4a" };

    // 3D 모델 확장자
    private static readonly HashSet<string> ModelExt = new(StringComparer.OrdinalIgnoreCase)
    { ".fbx", ".obj", ".blend", ".dae", ".3ds", ".dxf" };

    private static readonly Regex IsReadableRegex = new(
        @"^\s*isReadable:\s*(\d)", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex StreamingMipmapsRegex = new(
        @"^\s*streamingMipmaps:\s*(\d)", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex EnableMipMapRegex = new(
        @"^\s*enableMipMap:\s*(\d)", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex TextureTypeRegex = new(
        @"^\s*textureType:\s*(-?\d+)", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex MaxTextureSizeRegex = new(
        @"maxTextureSize:\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex TextureCompressionRegex = new(
        @"textureCompression:\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex LoadTypeRegex = new(
        @"^\s*loadType:\s*(\d+)", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex PreloadAudioDataRegex = new(
        @"^\s*preloadAudioData:\s*(\d+)", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex ForceToMonoRegex = new(
        @"^\s*forceToMono:\s*(\d+)", RegexOptions.Multiline | RegexOptions.Compiled);

    public List<ImportFinding> Analyze(List<AssetInfo> assets, string assetsPath, IProgress<string>? progress = null)
    {
        var findings = new List<ImportFinding>();
        var assetsRoot = assetsPath.TrimEnd('\\', '/');

        foreach (var asset in assets)
        {
            var ext = Path.GetExtension(asset.Path);
            bool isTex = TextureExt.Contains(ext);
            bool isAud = AudioExt.Contains(ext);
            bool isMdl = ModelExt.Contains(ext);
            if (!isTex && !isAud && !isMdl) continue;

            var metaPath = asset.Path + ".meta";
            if (!File.Exists(metaPath)) continue;

            string meta;
            try { meta = File.ReadAllText(metaPath); }
            catch { continue; }

            var rel = ToRelative(asset.Path, assetsRoot);

            if (isTex && meta.Contains("TextureImporter:", StringComparison.Ordinal))
                CheckTexture(asset, rel, meta, findings);
            else if (isAud && meta.Contains("AudioImporter:", StringComparison.Ordinal))
                CheckAudio(asset, rel, meta, findings);
            else if (isMdl && meta.Contains("ModelImporter:", StringComparison.Ordinal))
                CheckModel(asset, rel, meta, findings);
        }

        return findings;
    }

    private static void CheckTexture(AssetInfo asset, string rel, string meta, List<ImportFinding> findings)
    {
        int textureType = ParseInt(TextureTypeRegex, meta, 0); // 0=Default, 1=Normal, 8=Sprite, 2=Editor GUI
        bool isReadable = ParseInt(IsReadableRegex, meta, 0) == 1;
        bool enableMipMap = ParseInt(EnableMipMapRegex, meta, 0) == 1;
        bool streamingMipmaps = ParseInt(StreamingMipmapsRegex, meta, 0) == 1;

        var defaultBlock = ExtractPlatformBlock(meta, "DefaultTexturePlatform");
        int maxSize = defaultBlock != null ? ParseInt(MaxTextureSizeRegex, defaultBlock, 2048) : 2048;
        int compression = defaultBlock != null ? ParseInt(TextureCompressionRegex, defaultBlock, 1) : 1;

        // 1) R/W 활성화 — 메모리 2배 사용 (4MB 이상은 ERR)
        if (isReadable && asset.Size > 256 * 1024)
        {
            var sev = asset.Size > 4 * 1024 * 1024 ? "ERR" : "WARN";
            findings.Add(new ImportFinding
            {
                AssetPath = asset.Path, RelativePath = rel, Name = asset.Name,
                AssetKind = "텍스처", Size = asset.Size,
                Severity = sev, CheckId = "TEX_RW_ENABLED",
                Message = "Read/Write 활성화됨 — 메모리 2배 사용",
                Detail = sev == "ERR"
                    ? "큰 텍스처에 R/W 활성 = 메모리 낭비 심각. 즉시 비활성 권장"
                    : "런타임에서 픽셀을 읽지 않으면 비활성 권장"
            });
        }

        // 2) 비압축 (Default 플랫폼) — 4MB 이상이면 ERR
        if (compression == 0 && asset.Size > 256 * 1024)
        {
            var sev = asset.Size > 4 * 1024 * 1024 ? "ERR" : "WARN";
            findings.Add(new ImportFinding
            {
                AssetPath = asset.Path, RelativePath = rel, Name = asset.Name,
                AssetKind = "텍스처", Size = asset.Size,
                Severity = sev, CheckId = "TEX_UNCOMPRESSED",
                Message = "비압축 텍스처 — Compressed 권장",
                Detail = sev == "ERR"
                    ? "큰 텍스처가 비압축 = VRAM/디스크 모두 폭발. 압축 필수"
                    : "Default Platform: Compression=None"
            });
        }

        // 3) MaxSize 과다 — 16384 이상은 ERR
        if (maxSize >= 8192 && asset.Size > 2 * 1024 * 1024)
        {
            var sev = maxSize >= 16384 ? "ERR" : "WARN";
            findings.Add(new ImportFinding
            {
                AssetPath = asset.Path, RelativePath = rel, Name = asset.Name,
                AssetKind = "텍스처", Size = asset.Size,
                Severity = sev, CheckId = "TEX_MAXSIZE_HUGE",
                Message = $"MaxSize {maxSize} — 메모리 과다 가능",
                Detail = sev == "ERR"
                    ? $"MaxSize {maxSize}는 사실상 모바일에서 사용 불가. 2048~4096 필수"
                    : "실제 화면 표시 크기를 고려해 2048~4096 권장"
            });
        }

        // 4) Sprite / Editor GUI / Cursor에 Mipmap 활성화
        //    textureType: 8=Sprite, 2=Editor GUI, 7=Cursor, 5=GUI/2D
        if (enableMipMap && (textureType == 8 || textureType == 2 || textureType == 7 || textureType == 5))
        {
            findings.Add(new ImportFinding
            {
                AssetPath = asset.Path, RelativePath = rel, Name = asset.Name,
                AssetKind = "텍스처", Size = asset.Size,
                Severity = "INFO", CheckId = "TEX_MIPMAP_2D",
                Message = "2D/UI 텍스처에 Mipmap 활성화 (불필요)",
                Detail = $"textureType={textureType}, Mipmap=ON — 33% 추가 메모리"
            });
        }

        // 5) 큰 텍스처인데 Streaming Mipmaps 미사용 (8MB 이상)
        if (!streamingMipmaps && enableMipMap && asset.Size > 8 * 1024 * 1024)
        {
            findings.Add(new ImportFinding
            {
                AssetPath = asset.Path, RelativePath = rel, Name = asset.Name,
                AssetKind = "텍스처", Size = asset.Size,
                Severity = "INFO", CheckId = "TEX_STREAMING_OFF",
                Message = "큰 텍스처인데 Streaming Mipmaps 비활성",
                Detail = "Streaming Mipmaps 활성화 시 메모리 절감 가능"
            });
        }
    }

    private static void CheckAudio(AssetInfo asset, string rel, string meta, List<ImportFinding> findings)
    {
        int loadType = ParseInt(LoadTypeRegex, meta, 0); // 0=Decompress, 1=CompressedInMemory, 2=Streaming
        bool preload = ParseInt(PreloadAudioDataRegex, meta, 1) == 1;

        // 1) 큰 오디오인데 DecompressOnLoad — 5MB 이상은 ERR
        if (loadType == 0 && asset.Size > 1 * 1024 * 1024)
        {
            var sev = asset.Size > 5 * 1024 * 1024 ? "ERR" : "WARN";
            findings.Add(new ImportFinding
            {
                AssetPath = asset.Path, RelativePath = rel, Name = asset.Name,
                AssetKind = "오디오", Size = asset.Size,
                Severity = sev, CheckId = "AUD_DECOMPRESS_LARGE",
                Message = "큰 오디오인데 DecompressOnLoad — 메모리 사용 큼",
                Detail = sev == "ERR"
                    ? "5MB+ 오디오를 통째로 메모리에 로드 = 모바일에서 OOM 위험"
                    : "효과음은 CompressedInMemory, BGM은 Streaming 권장"
            });
        }

        // 2) BGM 사이즈인데 Streaming 아님 (5MB 이상)
        if (loadType != 2 && asset.Size > 5 * 1024 * 1024)
        {
            findings.Add(new ImportFinding
            {
                AssetPath = asset.Path, RelativePath = rel, Name = asset.Name,
                AssetKind = "오디오", Size = asset.Size,
                Severity = "INFO", CheckId = "AUD_NOT_STREAMING",
                Message = "큰 오디오에 Streaming 미사용",
                Detail = "장시간 BGM은 Streaming 권장 (디스크 직접 재생)"
            });
        }

        // 3) Preload 켜진 큰 파일
        if (preload && asset.Size > 2 * 1024 * 1024)
        {
            findings.Add(new ImportFinding
            {
                AssetPath = asset.Path, RelativePath = rel, Name = asset.Name,
                AssetKind = "오디오", Size = asset.Size,
                Severity = "INFO", CheckId = "AUD_PRELOAD_LARGE",
                Message = "Preload Audio Data 활성화된 큰 오디오",
                Detail = "씬 로드 시간이 길어질 수 있음"
            });
        }
    }

    private static void CheckModel(AssetInfo asset, string rel, string meta, List<ImportFinding> findings)
    {
        // 모델은 meshes 블록 안에 isReadable이 들어있음
        bool isReadable = ParseInt(IsReadableRegex, meta, 0) == 1;

        // 모델 R/W 활성화 — 2MB 이상은 ERR
        if (isReadable && asset.Size > 100 * 1024)
        {
            var sev = asset.Size > 2 * 1024 * 1024 ? "ERR" : "WARN";
            findings.Add(new ImportFinding
            {
                AssetPath = asset.Path, RelativePath = rel, Name = asset.Name,
                AssetKind = "3D 모델", Size = asset.Size,
                Severity = sev, CheckId = "MDL_RW_ENABLED",
                Message = "Mesh Read/Write 활성화됨 — 메모리 2배 사용",
                Detail = sev == "ERR"
                    ? "큰 메쉬에 R/W = 메모리 낭비 심각. 비활성 필수"
                    : "런타임에서 메쉬 수정·MeshCollider 동적 생성 안 하면 비활성"
            });
        }
    }

    /// <summary>
    /// platformSettings 안에서 특정 buildTarget 블록의 본문만 잘라낸다.
    /// "buildTarget: DefaultTexturePlatform" 부터 다음 "buildTarget:" 직전까지.
    /// </summary>
    private static string? ExtractPlatformBlock(string meta, string buildTarget)
    {
        var marker = "buildTarget: " + buildTarget;
        var start = meta.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;
        var next = meta.IndexOf("buildTarget:", start + marker.Length, StringComparison.Ordinal);
        return next > 0 ? meta[start..next] : meta[start..];
    }

    private static int ParseInt(Regex regex, string text, int fallback)
    {
        var m = regex.Match(text);
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : fallback;
    }

    private static string ToRelative(string fullPath, string assetsRoot)
    {
        if (fullPath.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
            return "Assets" + fullPath[assetsRoot.Length..].Replace('\\', '/');
        return fullPath.Replace('\\', '/');
    }
}
