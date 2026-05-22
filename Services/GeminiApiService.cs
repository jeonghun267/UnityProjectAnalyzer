using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityProjectAnalyzer.Services;

public class GeminiApiService
{
    private readonly HttpClient _http;
    private string _apiKey = "";
    private string _model = "gemini-2.5-flash";

    public GeminiApiService()
    {
        _http = new HttpClient();
        _http.Timeout = TimeSpan.FromSeconds(60);
    }

    public void SetApiKey(string key) => _apiKey = key?.Trim() ?? "";
    public void SetModel(string model)
    {
        if (!string.IsNullOrWhiteSpace(model)) _model = model.Trim();
    }
    public bool HasApiKey => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<string> SendMessageAsync(string userMessage, string? systemPrompt = null)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            return "⚠ API 키가 설정되지 않았습니다. 좌측 설정에서 GEMINI_API_KEY를 입력하세요.\n키 발급: https://aistudio.google.com/apikey";

        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";

            object? systemInstruction = null;
            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                systemInstruction = new
                {
                    parts = new[] { new { text = systemPrompt } }
                };
            }

            var requestBody = new
            {
                system_instruction = systemInstruction,
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = userMessage } }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.7,
                    maxOutputTokens = 1024
                }
            };

            var json = JsonConvert.SerializeObject(requestBody,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync(url, content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                var errMsg = TryExtractError(responseText) ?? responseText;
                return $"❌ API 오류 ({(int)response.StatusCode} {response.StatusCode}): {errMsg}";
            }

            var parsed = JObject.Parse(responseText);
            var text = parsed["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();
            if (string.IsNullOrWhiteSpace(text))
            {
                var blockReason = parsed["promptFeedback"]?["blockReason"]?.ToString();
                if (!string.IsNullOrWhiteSpace(blockReason))
                    return $"⚠ 응답이 차단되었습니다: {blockReason}";
                return "(응답 없음)";
            }
            return text!;
        }
        catch (TaskCanceledException)
        {
            return "❌ 요청 시간 초과 (60초)";
        }
        catch (Exception ex)
        {
            return $"❌ 오류: {ex.Message}";
        }
    }

    public Task<string> AnalyzeErrorAsync(string errorLog)
    {
        var prompt = $"다음 Unity 에러/이슈를 분석하고 해결 방법을 알려주세요:\n\n{errorLog}\n\n" +
                     "형식으로 답변:\n" +
                     "원인: ...\n" +
                     "해결책:\n  1) ...\n  2) ...\n" +
                     "참고: (관련 Unity 문서/패키지가 있다면)";
        return SendMessageAsync(prompt,
            "당신은 Unity 개발 전문가입니다. 한국어로 간결하고 실용적으로 답변하세요.");
    }

    /// <summary>
    /// 단일 .cs 파일 리뷰 — 로컬 휴리스틱 결과를 컨텍스트로 같이 넘긴다
    /// </summary>
    public Task<string> ReviewScriptAsync(string relativePath, string code, IEnumerable<string> localFindings)
    {
        var findingsText = localFindings.Any()
            ? "로컬 휴리스틱 진단:\n  - " + string.Join("\n  - ", localFindings)
            : "로컬 휴리스틱: 특이사항 없음";

        var prompt =
            $"파일: {relativePath}\n\n" +
            findingsText + "\n\n" +
            "위 파일을 Unity 베스트 프랙티스 관점에서 리뷰하고 다음 형식으로 답해줘:\n\n" +
            "## 한 줄 요약\n" +
            "전반적인 코드 품질과 가장 큰 이슈 1줄.\n\n" +
            "## 잘된 점 (있으면)\n" +
            "- ...\n\n" +
            "## 개선 제안 (우선순위 순)\n" +
            "1. **[심각도]** 무엇을, 왜, 어떻게.\n" +
            "   ```csharp\n   // 수정 예시 (필요한 부분만)\n   ```\n\n" +
            "## 한 단계 더\n" +
            "구조/네이밍/패턴 개선 아이디어 1-2개.\n\n" +
            "코드:\n```csharp\n" + code + "\n```";

        var system =
            "당신은 시니어 Unity C# 개발자입니다. 코드 리뷰는 한국어로, " +
            "구체적이고 실행 가능한 제안만. 모호한 일반론 금지. " +
            "MonoBehaviour 라이프사이클, 가비지, 직렬화, 메모리, async 패턴, " +
            "DI/이벤트 분리에 특히 민감하게. 코드는 짧게 ```csharp 블록으로.";

        return SendMessageAsync(prompt, system);
    }

    private static string? TryExtractError(string body)
    {
        try
        {
            var j = JObject.Parse(body);
            return j["error"]?["message"]?.ToString();
        }
        catch { return null; }
    }
}
