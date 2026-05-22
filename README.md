# Unity Project Analyzer

> Unity 프로젝트를 분석하고 Gemini AI로 이슈를 해결하는 WPF 데스크탑 툴

## 📖 프로젝트 소개

Unity 개발자를 위한 **외부 분석 도구**입니다. 프로젝트 폴더를 선택하면 에셋 구성·용량·잠재 이슈를 시각화하고, `.meta` GUID를 교차 분석해 실제 미사용 에셋을 판정합니다. Gemini API와 GitHub API를 통해 코드 리뷰, 빌드 에러 해결책, 커밋 히스토리를 한 화면에서 다룹니다.

## ✨ 주요 기능

### 1. Unity 프로젝트 자동 분석
- Unity 버전 자동 감지 (`ProjectSettings/ProjectVersion.txt`)
- Assets 폴더 전체 스캔, 카테고리(텍스처/3D/오디오/스크립트 등) 분류
- 폴더별 용량 Top 5, 도넛 차트로 카테고리 비중 시각화

### 2. GUID 기반 의존성 그래프 (정확한 미사용 판정)
- `.meta` 전체에서 GUID 인덱스 구성
- prefab / scene / material / controller / asset 등 YAML 에셋의 `guid:` 참조 추출
- Scene · Resources · StreamingAssets · Editor Default Resources · `.cs`/`.asmdef`를 루트로 BFS → 도달 불가능한 노드를 **미사용**으로 판정
- 좌측 에셋 리스트 + 우측 In/Out 참조 패널, 클릭 드릴다운
- **Gemini로 미사용 에셋 일괄 분석** · **CSV 내보내기** 지원

### 3. 코드 휴리스틱 분석 (스크립트)
- MonoBehaviour / ScriptableObject / Editor 자동 식별
- `Update` 안의 `GetComponent` / `Find*` / 매 프레임 객체 할당
- `public` 필드 남발, `async void`, 빈 `catch`, `Debug.Log` 과다, `GameObject.Find("…")` 매직 스트링
- 중복 MonoBehaviour 클래스명 탐지

### 4. Unity Editor.log 파싱 & 필터
- `%LOCALAPPDATA%\Unity\Editor\Editor.log` 마지막 800줄 로드
- ERR / WARN / INFO 자동 분류, 본문 텍스트 검색 필터 내장

### 5. GitHub 연동
- 저장소 커밋 히스토리(최근 15개) 로딩, 클릭 시 브라우저로 이동

### 6. Gemini AI 어시스턴트
- 프로젝트 컨텍스트(에셋 수·용량·이슈 수)를 system instruction에 자동 주입
- 이슈/스크립트 1개를 골라 즉시 코드 리뷰 요청 가능
- 모델 선택: `gemini-2.5-flash` / `gemini-2.5-pro` / `gemini-2.0-flash` / `gemini-flash-latest`

## 🛠 기술 스택

| 영역 | 기술 |
|---|---|
| 프레임워크 | .NET 8 + WPF |
| 아키텍처 | MVVM (CommunityToolkit.Mvvm) |
| HTTP | HttpClient |
| JSON | Newtonsoft.Json |
| API 연동 | Google Gemini API, GitHub REST API |

## 🚀 실행 방법

### 요구사항
- Windows 10 이상
- .NET 8.0 SDK ([다운로드](https://dotnet.microsoft.com/download/dotnet/8.0))

### 실행
```bash
dotnet restore
dotnet run
```

### Gemini API 키 설정
AI 기능을 쓰려면 필요합니다:
- 방법 1: 앱 좌측 사이드바 또는 설정 페이지에서 직접 입력 (`%APPDATA%\UnityProjectAnalyzer\config.json`에 저장됨)
- 방법 2: 환경변수 `GEMINI_API_KEY` 또는 `GOOGLE_API_KEY` 설정 후 실행

API 키 발급: https://aistudio.google.com/apikey

## 📂 프로젝트 구조

```
UnityProjectAnalyzer/
├── App.xaml / App.xaml.cs              # 앱 진입점
├── UnityProjectAnalyzer.csproj
├── Models/
│   └── DataModels.cs                   # 도메인 모델 (AssetInfo, IssueItem, AssetDependency 등)
├── Services/
│   ├── UnityProjectAnalyzer.cs         # 메인 분석 오케스트레이터
│   ├── DependencyAnalyzer.cs           # GUID 인덱스 + outgoing/incoming 그래프 + 미사용 BFS
│   ├── CodeAnalyzer.cs                 # 스크립트 휴리스틱
│   ├── BuildLogService.cs              # Editor.log 파서
│   ├── GeminiApiService.cs             # Gemini REST 클라이언트
│   ├── GitHubService.cs                # GitHub REST 클라이언트
│   └── AppSettingsService.cs           # config.json I/O
├── ViewModels/
│   └── MainViewModel.cs
├── Views/
│   ├── MainWindow.xaml(.cs)
│   ├── Theme.xaml                      # 다크 테마 리소스
│   └── Pages/                          # 8개 페이지 UserControl
│       ├── DashboardPage
│       ├── AssetsPage
│       ├── IssuesPage
│       ├── ScriptsPage
│       ├── DependencyPage              # GUID 의존성 그래프
│       ├── BuildLogPage
│       ├── GitPage
│       └── SettingsPage
└── Converters/
    └── Converters.cs                   # Severity/Bool/Role 컨버터
```

## 🎯 사용 흐름

1. **프로젝트 선택**: 좌측 상단 `[📁 프로젝트 열기]` → Unity 프로젝트 폴더 선택
2. **자동 분석**: 폴더 선택 후 자동 스캔 (수동 재분석은 `[▶ 재분석]`)
3. **결과 확인**: 대시보드에서 통계·도넛·이슈 리스트 확인
4. **의존성 그래프**: 미사용 의심 에셋 확인 → `✦ 미사용 AI 분석`으로 정리 우선순위 분석 → `⇣ CSV` 내보내기
5. **AI 분석**: 이슈/스크립트 1개 클릭 → 우측 채팅 영역에서 Gemini가 해결책 작성
6. **GitHub 연동**: 좌측 사이드바에 `owner/repo` 입력 → 커밋 15개 로드

## 🎨 Unity 개발 면접 어필 포인트

- **Unity 프로젝트 구조 이해**: Assets / ProjectSettings / `.meta` 체계
- **에셋 의존성 시스템**: GUID 인덱스 + YAML 참조 스캔 + reachability BFS
- **외부 도구 개발**: EditorWindow가 아닌 별도 데스크탑 앱 관점
- **REST API 통합**: GitHub + Gemini
- **MVVM 패턴**: CommunityToolkit.Mvvm `[ObservableProperty]` / `[RelayCommand]`
- **비동기 프로그래밍**: `async/await` + `Progress<T>` 진행률 보고

## 📝 라이선스

학습 및 포트폴리오 용도

## 👤 개발자

오정훈 - 폴리텍대학 VR/XR 콘텐츠개발과
