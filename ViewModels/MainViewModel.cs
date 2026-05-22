using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UnityProjectAnalyzer.Models;
using UnityProjectAnalyzer.Services;
using Microsoft.Win32;

namespace UnityProjectAnalyzer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly Services.UnityProjectAnalyzer _analyzer = new();
    private readonly GitHubService _github = new();
    private readonly GeminiApiService _gemini = new();
    private readonly BuildLogService _buildLogService = new();
    private readonly AppSettings _settings;

    // 프로젝트 정보
    [ObservableProperty] private string projectPath = "프로젝트를 선택하세요";
    [ObservableProperty] private string projectName = "(미선택)";
    [ObservableProperty] private string unityVersion = "-";
    [ObservableProperty] private string statusMessage = "준비됨";
    [ObservableProperty] private bool isAnalyzing;
    [ObservableProperty] private bool isChatting;

    // 통계
    [ObservableProperty] private int totalAssetCount;
    [ObservableProperty] private string totalSizeText = "0 MB";
    [ObservableProperty] private int unusedAssetCount;
    [ObservableProperty] private string unusedSizeText = "0 MB";
    [ObservableProperty] private int issueCount;

    // 컬렉션
    public ObservableCollection<CategoryStat> TopFolders { get; } = new();
    public ObservableCollection<IssueItem> Issues { get; } = new();
    public ObservableCollection<GitCommit> Commits { get; } = new();
    public ObservableCollection<ChatMessage> ChatMessages { get; } = new();
    public ObservableCollection<DonutSegment> DonutSegments { get; } = new();
    public ObservableCollection<DonutSegment> DonutLegend { get; } = new();
    public ObservableCollection<ScriptFile> Scripts { get; } = new();
    public ObservableCollection<AssetInfo> Assets { get; } = new();
    public ObservableCollection<BuildLogEntry> BuildLog { get; } = new();
    public ObservableCollection<NavItem> NavItems { get; } = new();
    public ObservableCollection<AssetDependency> DependencyNodes { get; } = new();
    public ObservableCollection<string> SelectedIncoming { get; } = new();
    public ObservableCollection<string> SelectedOutgoing { get; } = new();
    public ObservableCollection<ImportFinding> ImportFindings { get; } = new();

    private List<ScriptFile> _allScripts = new();
    private List<AssetInfo> _allAssets = new();
    private List<AssetDependency> _allDependencies = new();
    private List<ImportFinding> _allImportFindings = new();

    [ObservableProperty] private string importFilter = "";
    [ObservableProperty] private string importKindFilter = "전체";
    public ObservableCollection<string> ImportKindOptions { get; } = new()
    {
        "전체", "텍스처", "오디오", "3D 모델"
    };
    partial void OnImportFilterChanged(string value) => ApplyImportFilter();
    partial void OnImportKindFilterChanged(string value) => ApplyImportFilter();
    public bool HasImportFindings => ImportFindings.Count > 0;

    [ObservableProperty] private string dependencyFilter = "";
    [ObservableProperty] private bool showOnlyUnused = false;
    [ObservableProperty] private AssetDependency? selectedDependency;
    [ObservableProperty] private string dependencySummary = "";

    partial void OnDependencyFilterChanged(string value) => ApplyDependencyFilter();
    partial void OnShowOnlyUnusedChanged(bool value) => ApplyDependencyFilter();
    partial void OnSelectedDependencyChanged(AssetDependency? value)
    {
        SelectedIncoming.Clear();
        SelectedOutgoing.Clear();
        if (value == null) return;
        foreach (var p in value.IncomingPaths) SelectedIncoming.Add(p);
        foreach (var p in value.OutgoingPaths) SelectedOutgoing.Add(p);
        OnPropertyChanged(nameof(HasSelectedDependency));
    }
    public bool HasSelectedDependency => SelectedDependency != null;
    public bool HasDependencies => DependencyNodes.Count > 0;

    // 빌드 로그 필터
    private List<BuildLogEntry> _allBuildLog = new();
    [ObservableProperty] private string buildLogSeverityFilter = "전체";
    public ObservableCollection<string> BuildLogSeverityOptions { get; } = new()
    {
        "전체", "ERR", "WARN", "INFO"
    };
    [ObservableProperty] private string buildLogTextFilter = "";
    partial void OnBuildLogSeverityFilterChanged(string value) => ApplyBuildLogFilter();
    partial void OnBuildLogTextFilterChanged(string value) => ApplyBuildLogFilter();

    [ObservableProperty] private string scriptFilter = "";
    [ObservableProperty] private bool showOnlyWithFindings = false;
    [ObservableProperty] private int scriptCount;

    [ObservableProperty] private string assetFilter = "";
    [ObservableProperty] private string assetCategoryFilter = "전체";
    public ObservableCollection<string> AssetCategories { get; } = new() { "전체" };

    partial void OnScriptFilterChanged(string value) => ApplyScriptFilter();
    partial void OnShowOnlyWithFindingsChanged(bool value) => ApplyScriptFilter();
    partial void OnAssetFilterChanged(string value) => ApplyAssetFilter();
    partial void OnAssetCategoryFilterChanged(string value) => ApplyAssetFilter();

    // 빈 상태 플래그
    public bool HasTopFolders => TopFolders.Count > 0;
    public bool HasIssues => Issues.Count > 0;
    public bool HasCommits => Commits.Count > 0;
    public bool HasDonut => DonutSegments.Count > 0;
    public bool HasScripts => Scripts.Count > 0;
    public bool HasAssets => Assets.Count > 0;
    public bool HasBuildLog => BuildLog.Count > 0;

    // GitHub 설정
    [ObservableProperty] private string githubRepo = "";

    // Gemini 설정
    [ObservableProperty] private string apiKey = "";
    [ObservableProperty] private string chatInput = "";
    [ObservableProperty] private bool hasApiKey;
    [ObservableProperty] private string geminiModel = "gemini-2.5-flash";
    public ObservableCollection<string> GeminiModelOptions { get; } = new()
    {
        "gemini-2.5-flash", "gemini-2.5-pro", "gemini-2.0-flash", "gemini-flash-latest"
    };
    partial void OnGeminiModelChanged(string value)
    {
        _gemini.SetModel(value);
        _settings.GeminiModel = value;
        AppSettingsService.Save(_settings);
    }

    // 빌드 로그 메타
    [ObservableProperty] private string buildLogPath = "";
    [ObservableProperty] private string buildLogLastWrite = "";

    // 페이지 전환
    [ObservableProperty] private AppPage currentPage = AppPage.Dashboard;
    public bool IsDashboard => CurrentPage == AppPage.Dashboard;
    public bool IsAssetsPage => CurrentPage == AppPage.Assets;
    public bool IsIssuesPage => CurrentPage == AppPage.Issues;
    public bool IsScriptsPage => CurrentPage == AppPage.Scripts;
    public bool IsDependencyPage => CurrentPage == AppPage.Dependency;
    public bool IsImportSettingsPage => CurrentPage == AppPage.ImportSettings;
    public bool IsBuildLogPage => CurrentPage == AppPage.BuildLog;
    public bool IsGitPage => CurrentPage == AppPage.Git;
    public bool IsSettingsPage => CurrentPage == AppPage.Settings;
    public string CurrentPageTitle => CurrentPage switch
    {
        AppPage.Dashboard => "대시보드",
        AppPage.Assets => "에셋 탐색기",
        AppPage.Issues => "이슈 분석",
        AppPage.Scripts => "스크립트 코드 분석",
        AppPage.Dependency => "의존성 그래프",
        AppPage.ImportSettings => "에셋 임포트 설정",
        AppPage.BuildLog => "빌드 로그",
        AppPage.Git => "Git 히스토리",
        AppPage.Settings => "설정",
        _ => ""
    };
    partial void OnCurrentPageChanged(AppPage value)
    {
        OnPropertyChanged(nameof(IsDashboard));
        OnPropertyChanged(nameof(IsAssetsPage));
        OnPropertyChanged(nameof(IsIssuesPage));
        OnPropertyChanged(nameof(IsScriptsPage));
        OnPropertyChanged(nameof(IsDependencyPage));
        OnPropertyChanged(nameof(IsImportSettingsPage));
        OnPropertyChanged(nameof(IsBuildLogPage));
        OnPropertyChanged(nameof(IsGitPage));
        OnPropertyChanged(nameof(IsSettingsPage));
        OnPropertyChanged(nameof(CurrentPageTitle));
        foreach (var n in NavItems) n.IsActive = n.Page == value.ToString();

        // 페이지 진입 시 lazy load
        if (value == AppPage.BuildLog && BuildLog.Count == 0) _ = Task.Run(RefreshBuildLog);
        if (value == AppPage.Git && Commits.Count == 0 && !string.IsNullOrWhiteSpace(GithubRepo))
            _ = LoadCommitsAsync();
    }

    // 채팅 스크롤 신호 (View가 구독)
    public event EventHandler? ChatScrollRequested;

    public MainViewModel()
    {
        _settings = AppSettingsService.Load();

        // 초기 메시지
        ChatMessages.Add(new ChatMessage
        {
            Role = "assistant",
            Content = "안녕하세요! Unity 프로젝트 분석을 도와드리는 AI 어시스턴트(Gemini)입니다.\n\n" +
                      "1) [프로젝트 열기]로 Unity 폴더 선택\n" +
                      "2) 자동 분석 결과 확인\n" +
                      "3) 이슈 클릭하면 AI가 원인/해결책 분석\n" +
                      "4) 좌측 하단에서 GEMINI_API_KEY 입력 (https://aistudio.google.com/apikey)"
        });

        // 설정 자동 로드 (파일 → 환경변수 순)
        if (!string.IsNullOrWhiteSpace(_settings.GeminiApiKey))
        {
            ApiKey = _settings.GeminiApiKey;
            _gemini.SetApiKey(_settings.GeminiApiKey);
            HasApiKey = true;
        }
        else
        {
            var envKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
                      ?? Environment.GetEnvironmentVariable("GOOGLE_API_KEY");
            if (!string.IsNullOrWhiteSpace(envKey))
            {
                ApiKey = envKey;
                _gemini.SetApiKey(envKey);
                HasApiKey = true;
            }
        }

        GeminiModel = string.IsNullOrWhiteSpace(_settings.GeminiModel) ? "gemini-2.5-flash" : _settings.GeminiModel;
        _gemini.SetModel(GeminiModel);

        if (!string.IsNullOrWhiteSpace(_settings.GithubRepo))
            GithubRepo = _settings.GithubRepo;

        // 사이드바 nav
        NavItems.Add(new NavItem { Page = "Dashboard",  Icon = "⬡", Label = "대시보드",       IsActive = true });
        NavItems.Add(new NavItem { Page = "Assets",     Icon = "📁", Label = "에셋 탐색기" });
        NavItems.Add(new NavItem { Page = "Issues",     Icon = "⚠", Label = "이슈 분석" });
        NavItems.Add(new NavItem { Page = "Scripts",    Icon = "ⓢ", Label = "스크립트 분석" });
        NavItems.Add(new NavItem { Page = "Dependency", Icon = "🔗", Label = "의존성 그래프" });
        NavItems.Add(new NavItem { Page = "ImportSettings", Icon = "🖼", Label = "임포트 설정" });
        NavItems.Add(new NavItem { Page = "BuildLog",   Icon = "🏗", Label = "빌드 로그" });
        NavItems.Add(new NavItem { Page = "Git",        Icon = "⎇", Label = "Git 히스토리" });
        NavItems.Add(new NavItem { Page = "Settings",   Icon = "⚙", Label = "설정" });

        // 마지막 프로젝트 자동 로드
        if (!string.IsNullOrWhiteSpace(_settings.LastProjectPath)
            && _analyzer.IsUnityProject(_settings.LastProjectPath))
        {
            ProjectPath = _settings.LastProjectPath;
            ProjectName = Path.GetFileName(_settings.LastProjectPath);
            _ = AnalyzeAsync();
        }

        // 컬렉션 변경 시 빈 상태 갱신
        TopFolders.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasTopFolders));
        Issues.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasIssues));
            UpdateNavBadges();
        };
        Commits.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasCommits));
        DonutSegments.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasDonut));
        Scripts.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasScripts));
        Assets.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasAssets));
        BuildLog.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasBuildLog));
        DependencyNodes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasDependencies));
        ImportFindings.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasImportFindings));
        ChatMessages.CollectionChanged += (_, _) => ChatScrollRequested?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateNavBadges()
    {
        var issueNav = NavItems.FirstOrDefault(n => n.Page == "Issues");
        if (issueNav != null)
        {
            issueNav.Badge = Issues.Count > 0 ? Issues.Count.ToString() : "";
            // 가장 높은 심각도에 맞춰 배지 색 결정
            issueNav.BadgeStyle = Issues.Any(i => i.Severity == "ERR") ? "danger"
                                : Issues.Any(i => i.Severity == "WARN") ? "warn"
                                : "info";
        }

        var scriptNav = NavItems.FirstOrDefault(n => n.Page == "Scripts");
        if (scriptNav != null)
        {
            var withFindings = _allScripts.Count(s => s.FindingCount > 0);
            scriptNav.Badge = withFindings > 0 ? withFindings.ToString() : "";
            scriptNav.BadgeStyle = "warn";
        }

        var depNav = NavItems.FirstOrDefault(n => n.Page == "Dependency");
        if (depNav != null)
        {
            var unused = _allDependencies.Count(d => d.IsUnused);
            depNav.Badge = unused > 0 ? unused.ToString() : "";
            depNav.BadgeStyle = "warn";
        }

        var importNav = NavItems.FirstOrDefault(n => n.Page == "ImportSettings");
        if (importNav != null)
        {
            var n = _allImportFindings.Count;
            importNav.Badge = n > 0 ? n.ToString() : "";
            importNav.BadgeStyle = "warn";
        }
    }

    [RelayCommand]
    private void OpenProject()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Unity 프로젝트 폴더 선택"
        };

        if (dlg.ShowDialog() == true)
        {
            if (!_analyzer.IsUnityProject(dlg.FolderName))
            {
                MessageBox.Show("Unity 프로젝트가 아닙니다.\n(Assets 폴더와 ProjectSettings 폴더가 필요합니다)",
                    "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ProjectPath = dlg.FolderName;
            ProjectName = Path.GetFileName(dlg.FolderName);
            _settings.LastProjectPath = dlg.FolderName;
            AppSettingsService.Save(_settings);
            _ = AnalyzeAsync();
        }
    }

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (!_analyzer.IsUnityProject(ProjectPath))
        {
            StatusMessage = "프로젝트를 먼저 선택하세요";
            return;
        }

        IsAnalyzing = true;
        try
        {
            var progress = new Progress<string>(msg => StatusMessage = msg);
            var result = await _analyzer.AnalyzeAsync(ProjectPath, progress);

            UnityVersion = result.UnityVersion;
            TotalAssetCount = result.TotalAssetCount;
            TotalSizeText = CategoryStat.FormatSize(result.TotalSize);
            UnusedAssetCount = result.UnusedAssetCount;
            UnusedSizeText = CategoryStat.FormatSize(result.UnusedSize);
            IssueCount = result.Issues.Count;

            TopFolders.Clear();
            foreach (var folder in result.TopFolders)
                TopFolders.Add(folder);

            Issues.Clear();
            foreach (var issue in result.Issues)
                Issues.Add(issue);

            _allScripts = result.Scripts;
            ScriptCount = _allScripts.Count;
            ApplyScriptFilter();

            _allAssets = result.Assets;
            ApplyAssetFilter();

            // 카테고리 옵션 갱신
            AssetCategories.Clear();
            AssetCategories.Add("전체");
            foreach (var c in result.Categories.Select(c => c.Name))
                AssetCategories.Add(c);

            // 의존성 그래프 결과 반영
            _allDependencies = result.Dependencies.Nodes;
            SelectedDependency = null;
            DependencySummary = $"{result.Dependencies.GuidCount}개 GUID · {result.Dependencies.ScannedRefCount}개 참조 · 미사용 {result.Dependencies.UnusedCount}개";
            ApplyDependencyFilter();

            // 임포트 설정 진단 반영
            _allImportFindings = result.ImportFindings;
            ApplyImportFilter();

            BuildDonut(result.Categories, result.TotalSize);
            UpdateNavBadges();

            StatusMessage = $"✓ 분석 완료 — {TotalAssetCount}개 에셋 · 스크립트 {ScriptCount}개 · {TotalSizeText}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ 오류: {ex.Message}";
            MessageBox.Show(ex.Message, "분석 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    /// <summary>
    /// 도넛 차트 세그먼트 계산.
    /// Ellipse 둘레는 stroke width 배수로 환산됨 (StrokeDashArray의 단위가 그러함).
    /// </summary>
    private const double DonutDiameter = 160;
    private const double DonutStroke = 22;
    // 둘레 = π * D, StrokeDashArray 단위로 환산
    private static readonly double DonutCircUnits = Math.PI * DonutDiameter / DonutStroke;

    // GitHub Dark 시그니처 컬러
    private static readonly string[] DonutColors =
    {
        "#58a6ff", "#3fb950", "#d29922", "#f85149",
        "#bc8cff", "#79c0ff", "#f0883e", "#a371f7"
    };

    private void BuildDonut(List<CategoryStat> categories, long totalSize)
    {
        DonutSegments.Clear();
        DonutLegend.Clear();

        if (categories.Count == 0 || totalSize <= 0) return;

        double cumulative = 0;
        for (int i = 0; i < categories.Count; i++)
        {
            var cat = categories[i];
            double percent = (double)cat.TotalSize / totalSize;
            double visibleUnits = percent * DonutCircUnits;
            double hiddenUnits = Math.Max(0, DonutCircUnits - visibleUnits);

            var seg = new DonutSegment
            {
                Name = cat.Name,
                Color = DonutColors[i % DonutColors.Length],
                DashArray = new DoubleCollection { visibleUnits, hiddenUnits },
                StartAngle = -90 + cumulative * 360,
                Percent = percent,
                SizeText = cat.SizeText
            };
            DonutSegments.Add(seg);
            DonutLegend.Add(seg);

            cumulative += percent;
        }
    }

    [RelayCommand]
    private async Task LoadCommitsAsync()
    {
        if (string.IsNullOrWhiteSpace(GithubRepo))
        {
            MessageBox.Show("GitHub 저장소를 입력하세요\n예: yeonyeong0120/Repo_Artti",
                "안내", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        StatusMessage = "GitHub 커밋 로딩 중...";
        var commits = await _github.GetCommitsAsync(GithubRepo, 15);
        Commits.Clear();
        foreach (var c in commits) Commits.Add(c);
        StatusMessage = $"✓ {commits.Count}개 커밋 로드";

        _settings.GithubRepo = GithubRepo;
        AppSettingsService.Save(_settings);
    }

    [RelayCommand]
    private void OpenCommit(GitCommit? commit)
    {
        if (commit == null || string.IsNullOrWhiteSpace(commit.Url)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = commit.Url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ 링크 열기 실패: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SaveApiKey()
    {
        _gemini.SetApiKey(ApiKey);
        HasApiKey = _gemini.HasApiKey;
        _settings.GeminiApiKey = ApiKey ?? "";
        AppSettingsService.Save(_settings);
        StatusMessage = string.IsNullOrWhiteSpace(ApiKey) ? "API 키 제거됨" : "✓ Gemini API 키 저장됨";
    }

    [RelayCommand]
    private void OpenApiKeyPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://aistudio.google.com/apikey",
                UseShellExecute = true
            });
        }
        catch { }
    }

    [RelayCommand]
    private void Navigate(string? page)
    {
        if (string.IsNullOrWhiteSpace(page)) return;
        if (Enum.TryParse<AppPage>(page, out var p))
            CurrentPage = p;
    }

    [RelayCommand]
    private void RefreshBuildLog()
    {
        var (entries, source, lastWrite) = _buildLogService.ReadLatest();
        App.Current?.Dispatcher.Invoke(() =>
        {
            _allBuildLog = entries;
            ApplyBuildLogFilter();
            BuildLogPath = string.IsNullOrEmpty(source) ? "Editor.log 없음" : source;
            BuildLogLastWrite = lastWrite?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
            StatusMessage = $"✓ Editor.log {entries.Count}줄 로드";
        });
    }

    private void ApplyBuildLogFilter()
    {
        BuildLog.Clear();
        IEnumerable<BuildLogEntry> q = _allBuildLog;
        if (BuildLogSeverityFilter != "전체")
            q = q.Where(e => e.Severity == BuildLogSeverityFilter);
        if (!string.IsNullOrWhiteSpace(BuildLogTextFilter))
        {
            var f = BuildLogTextFilter.Trim();
            q = q.Where(e => e.Message.Contains(f, StringComparison.OrdinalIgnoreCase));
        }
        foreach (var e in q) BuildLog.Add(e);
    }

    [RelayCommand]
    private void OpenConfigFolder()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "UnityProjectAnalyzer");
        Directory.CreateDirectory(dir);
        try
        {
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ 폴더 열기 실패: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenEditorLog()
    {
        var path = BuildLogService.DefaultLogPath;
        if (!File.Exists(path)) path = BuildLogService.PrevLogPath;
        if (!File.Exists(path))
        {
            MessageBox.Show("Editor.log 파일을 찾을 수 없습니다.", "안내",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch { }
    }

    private void ApplyAssetFilter()
    {
        Assets.Clear();
        IEnumerable<AssetInfo> q = _allAssets;
        if (AssetCategoryFilter != "전체")
            q = q.Where(a => a.Category == AssetCategoryFilter);
        if (!string.IsNullOrWhiteSpace(AssetFilter))
        {
            var f = AssetFilter.Trim();
            q = q.Where(a => a.Name.Contains(f, StringComparison.OrdinalIgnoreCase)
                          || a.Path.Contains(f, StringComparison.OrdinalIgnoreCase));
        }
        foreach (var a in q.OrderByDescending(x => x.Size).Take(500)) Assets.Add(a);
    }

    private void ApplyImportFilter()
    {
        ImportFindings.Clear();
        IEnumerable<ImportFinding> q = _allImportFindings;
        if (ImportKindFilter != "전체")
            q = q.Where(f => f.AssetKind == ImportKindFilter);
        if (!string.IsNullOrWhiteSpace(ImportFilter))
        {
            var f = ImportFilter.Trim();
            q = q.Where(x =>
                x.Name.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                x.RelativePath.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                x.Message.Contains(f, StringComparison.OrdinalIgnoreCase));
        }
        // ERR → WARN → INFO 순, 큰 파일 우선
        foreach (var x in q
            .OrderBy(f => f.Severity == "ERR" ? 0 : f.Severity == "WARN" ? 1 : 2)
            .ThenByDescending(f => f.Size)
            .Take(500))
            ImportFindings.Add(x);
    }

    /// <summary>
    /// 임포트 진단 1건을 Gemini에 보내 구체적 수정 절차 받기
    /// </summary>
    [RelayCommand]
    private async Task AnalyzeImportFindingAsync(ImportFinding? finding)
    {
        if (finding == null || IsChatting) return;
        if (!_gemini.HasApiKey)
        {
            MessageBox.Show("Gemini API 키를 먼저 입력하세요. (좌측 하단)",
                "안내", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var prompt =
            $"Unity 에셋 임포트 설정 진단:\n" +
            $"- 파일: {finding.RelativePath} ({finding.SizeText})\n" +
            $"- 종류: {finding.AssetKind}\n" +
            $"- 문제: {finding.Message}\n" +
            $"- 상세: {finding.Detail}\n\n" +
            "다음 형식으로 답해줘:\n" +
            "## 왜 문제인가\n" +
            "## Unity Inspector에서 수정 절차 (메뉴 경로 명시)\n" +
            "## 플랫폼별 권장 값 (PC/Android/iOS)\n" +
            "## 주의점";

        ChatMessages.Add(new ChatMessage
        {
            Role = "user",
            Content = $"✦ 임포트 진단: {finding.Name} — {finding.Message}"
        });
        ChatMessages.Add(new ChatMessage { Role = "assistant", Content = "Gemini 분석 중..." });

        IsChatting = true;
        StatusMessage = $"Gemini 분석 중: {finding.Name}";
        try
        {
            var response = await _gemini.SendMessageAsync(prompt, BuildSystemPrompt());
            ChatMessages.RemoveAt(ChatMessages.Count - 1);
            ChatMessages.Add(new ChatMessage { Role = "assistant", Content = response });
            StatusMessage = $"✓ 진단 분석 완료";
        }
        finally
        {
            IsChatting = false;
        }
    }

    private void ApplyDependencyFilter()
    {
        DependencyNodes.Clear();
        IEnumerable<AssetDependency> q = _allDependencies;
        if (ShowOnlyUnused) q = q.Where(d => d.IsUnused);
        if (!string.IsNullOrWhiteSpace(DependencyFilter))
        {
            var f = DependencyFilter.Trim();
            q = q.Where(d =>
                d.Name.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                d.RelativePath.Contains(f, StringComparison.OrdinalIgnoreCase));
        }
        // 미사용 먼저 → 참조 많은 순 → 이름 순
        foreach (var d in q
            .OrderByDescending(x => x.IsUnused)
            .ThenByDescending(x => x.IncomingCount)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Take(500))
            DependencyNodes.Add(d);
    }

    private void ApplyScriptFilter()
    {
        Scripts.Clear();
        IEnumerable<ScriptFile> q = _allScripts;
        if (ShowOnlyWithFindings) q = q.Where(s => s.FindingCount > 0);
        if (!string.IsNullOrWhiteSpace(ScriptFilter))
        {
            var f = ScriptFilter.Trim();
            q = q.Where(s =>
                s.Name.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                s.RelativePath.Contains(f, StringComparison.OrdinalIgnoreCase));
        }
        foreach (var s in q.Take(200)) Scripts.Add(s);
    }

    /// <summary>
    /// 스크립트 1개를 Gemini에게 리뷰 요청 — 결과는 채팅에 표시
    /// </summary>
    [RelayCommand]
    private async Task ReviewScriptAsync(ScriptFile? script)
    {
        if (script == null || IsChatting) return;

        if (!_gemini.HasApiKey)
        {
            MessageBox.Show("Gemini API 키를 먼저 입력하세요. (좌측 하단)",
                "안내", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string code;
        try
        {
            var fi = new FileInfo(script.Path);
            if (fi.Length > CodeAnalyzer.MaxAiBytes)
            {
                // 너무 크면 앞부분만 잘라서 보냄
                using var sr = new StreamReader(script.Path);
                var buf = new char[CodeAnalyzer.MaxAiBytes];
                var read = await sr.ReadAsync(buf, 0, buf.Length);
                code = new string(buf, 0, read) + "\n\n// [... 파일이 너무 커서 64KB까지만 전송됨]";
            }
            else
            {
                code = await File.ReadAllTextAsync(script.Path);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ 파일 읽기 실패: {ex.Message}";
            return;
        }

        ChatMessages.Add(new ChatMessage
        {
            Role = "user",
            Content = $"✦ 코드 리뷰: {script.RelativePath} ({script.LineCount}줄)"
        });
        ChatMessages.Add(new ChatMessage { Role = "assistant", Content = "리뷰 작성 중... (수 초 소요)" });

        IsChatting = true;
        StatusMessage = $"Gemini 리뷰 중: {script.Name}";
        try
        {
            var response = await _gemini.ReviewScriptAsync(script.RelativePath, code, script.Findings);
            ChatMessages.RemoveAt(ChatMessages.Count - 1);
            ChatMessages.Add(new ChatMessage { Role = "assistant", Content = response });
            StatusMessage = $"✓ 리뷰 완료: {script.Name}";
        }
        finally
        {
            IsChatting = false;
        }
    }

    /// <summary>
    /// 스크립트를 OS 기본 편집기로 열기
    /// </summary>
    /// <summary>
    /// Windows 탐색기에서 해당 파일을 선택된 상태로 연다 (AssetInfo / AssetDependency 공용)
    /// </summary>
    [RelayCommand]
    private void OpenInExplorer(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return;

        // 상대경로(Assets/...)가 들어오면 ProjectPath 기준으로 풀어줌
        var path = fullPath;
        if (!Path.IsPathRooted(path))
        {
            if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("Assets\\", StringComparison.OrdinalIgnoreCase))
            {
                path = Path.Combine(ProjectPath, path.Replace('/', Path.DirectorySeparatorChar));
            }
            else return;
        }

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            StatusMessage = $"❌ 파일 없음: {path}";
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ 탐색기 열기 실패: {ex.Message}";
        }
    }

    /// <summary>
    /// 의존성 페이지의 In/Out 리스트에서 한 항목을 클릭하면 해당 에셋으로 점프(드릴다운)
    /// </summary>
    [RelayCommand]
    private void DrillToDependency(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return;
        var target = _allDependencies.FirstOrDefault(d =>
            string.Equals(d.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        if (target == null) return;

        // 현재 필터가 일치 항목을 가리는 경우 필터 해제
        if (!DependencyNodes.Contains(target))
        {
            DependencyFilter = "";
            ShowOnlyUnused = false;
        }
        SelectedDependency = target;
    }

    /// <summary>
    /// 미사용 에셋 N개를 한 번에 Gemini에 보내 분류/우선순위 분석
    /// </summary>
    [RelayCommand]
    private async Task AnalyzeUnusedAssetsAsync()
    {
        if (IsChatting) return;
        var unused = _allDependencies.Where(d => d.IsUnused).ToList();
        if (unused.Count == 0)
        {
            StatusMessage = "미사용 에셋이 없습니다 ✨";
            return;
        }
        if (!_gemini.HasApiKey)
        {
            MessageBox.Show("Gemini API 키를 먼저 입력하세요. (좌측 하단)",
                "안내", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        CurrentPage = AppPage.Dashboard;

        var totalBytes = unused.Sum(u => u.Size);
        var sample = unused.OrderByDescending(u => u.Size).Take(40);
        var listText = string.Join("\n", sample.Select(u =>
            $"- {u.RelativePath} ({u.Category}, {u.SizeText})"));

        var prompt =
            $"Unity 프로젝트에서 어떤 씬/Resources/StreamingAssets로도 도달하지 않는 '미사용 의심' 에셋이 {unused.Count}개 발견되었어. " +
            $"총 용량은 약 {CategoryStat.FormatSize(totalBytes)}야.\n\n" +
            $"용량 큰 순 상위 {sample.Count()}개:\n{listText}\n\n" +
            "다음 형식으로 분석해줘:\n" +
            "## 정리 우선순위 (가장 안전하게 지울 수 있는 것 먼저)\n" +
            "## 진짜 미사용인지 의심되는 케이스 (코드에서 동적으로 로드 가능한 것 등)\n" +
            "## 권장 정리 절차 (백업/이름변경 → 빌드 검증 등)\n";

        ChatMessages.Add(new ChatMessage
        {
            Role = "user",
            Content = $"✦ 미사용 에셋 {unused.Count}개 ({CategoryStat.FormatSize(totalBytes)}) 분석 요청"
        });
        ChatMessages.Add(new ChatMessage { Role = "assistant", Content = "Gemini 분석 중..." });

        IsChatting = true;
        StatusMessage = "Gemini로 미사용 에셋 분석 중...";
        try
        {
            var response = await _gemini.SendMessageAsync(prompt, BuildSystemPrompt());
            ChatMessages.RemoveAt(ChatMessages.Count - 1);
            ChatMessages.Add(new ChatMessage { Role = "assistant", Content = response });
            StatusMessage = "✓ 미사용 에셋 분석 완료";
        }
        finally
        {
            IsChatting = false;
        }
    }

    /// <summary>
    /// 의존성 그래프 결과를 CSV로 저장
    /// </summary>
    [RelayCommand]
    private void ExportDependencyCsv()
    {
        if (_allDependencies.Count == 0)
        {
            StatusMessage = "내보낼 의존성 데이터가 없습니다";
            return;
        }
        var dlg = new SaveFileDialog
        {
            Title = "의존성 그래프 CSV 내보내기",
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"dependency_{DateTime.Now:yyyyMMdd_HHmm}.csv"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            using var w = new StreamWriter(dlg.FileName, false, new System.Text.UTF8Encoding(true));
            w.WriteLine("Path,Category,SizeBytes,IncomingCount,OutgoingCount,IsUnused,GUID");
            foreach (var d in _allDependencies)
            {
                w.WriteLine(string.Join(",",
                    CsvEscape(d.RelativePath),
                    CsvEscape(d.Category),
                    d.Size,
                    d.IncomingCount,
                    d.OutgoingCount,
                    d.IsUnused ? "1" : "0",
                    d.Guid));
            }
            StatusMessage = $"✓ CSV 저장됨: {Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ CSV 저장 실패: {ex.Message}";
        }
    }

    private static string CsvEscape(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var needs = s.Contains(',') || s.Contains('"') || s.Contains('\n');
        if (!needs) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    [RelayCommand]
    private void OpenScriptFile(ScriptFile? script)
    {
        if (script == null || !File.Exists(script.Path)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = script.Path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ 파일 열기 실패: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SendChatAsync()
    {
        if (string.IsNullOrWhiteSpace(ChatInput) || IsChatting) return;

        var userMsg = ChatInput;
        ChatMessages.Add(new ChatMessage { Role = "user", Content = userMsg });
        ChatInput = "";
        ChatMessages.Add(new ChatMessage { Role = "assistant", Content = "분석 중..." });

        IsChatting = true;
        try
        {
            var systemPrompt = BuildSystemPrompt();
            var response = await _gemini.SendMessageAsync(userMsg, systemPrompt);

            if (ChatMessages.Count > 0)
                ChatMessages.RemoveAt(ChatMessages.Count - 1);
            ChatMessages.Add(new ChatMessage { Role = "assistant", Content = response });
        }
        finally
        {
            IsChatting = false;
        }
    }

    [RelayCommand]
    private async Task AnalyzeIssueAsync(IssueItem? issue)
    {
        if (issue == null || IsChatting) return;
        ChatMessages.Add(new ChatMessage
        {
            Role = "user",
            Content = $"이 이슈 분석해줘: {issue.Message}"
        });
        ChatMessages.Add(new ChatMessage { Role = "assistant", Content = "분석 중..." });

        IsChatting = true;
        try
        {
            var response = await _gemini.AnalyzeErrorAsync(issue.Message);
            ChatMessages.RemoveAt(ChatMessages.Count - 1);
            ChatMessages.Add(new ChatMessage { Role = "assistant", Content = response });
        }
        finally
        {
            IsChatting = false;
        }
    }

    [RelayCommand]
    private void ClearChat()
    {
        ChatMessages.Clear();
        ChatMessages.Add(new ChatMessage
        {
            Role = "assistant",
            Content = "채팅이 초기화되었습니다. 새로 질문해주세요."
        });
    }

    private string BuildSystemPrompt()
    {
        return "당신은 Unity 개발 전문가입니다. 현재 프로젝트:\n" +
               $"- 이름: {ProjectName}\n" +
               $"- Unity 버전: {UnityVersion}\n" +
               $"- 에셋 수: {TotalAssetCount}\n" +
               $"- 용량: {TotalSizeText}\n" +
               $"- 이슈: {IssueCount}개\n\n" +
               "한국어로 간결하고 실용적으로 답변하세요. 코드 예시는 ```csharp 블록으로 감싸세요.";
    }
}
