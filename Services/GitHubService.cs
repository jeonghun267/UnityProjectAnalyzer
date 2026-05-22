using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json.Linq;
using UnityProjectAnalyzer.Models;

namespace UnityProjectAnalyzer.Services;

public class GitHubService
{
    private readonly HttpClient _http;

    public GitHubService()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("UnityProjectAnalyzer", "1.0"));
    }

    /// <summary>
    /// owner/repo 형식 또는 GitHub URL에서 커밋 가져오기
    /// </summary>
    public async Task<List<GitCommit>> GetCommitsAsync(string ownerRepo, int count = 10)
    {
        try
        {
            // URL 정리
            ownerRepo = ownerRepo.Replace("https://github.com/", "")
                                 .Replace("http://github.com/", "")
                                 .TrimEnd('/');

            var url = $"https://api.github.com/repos/{ownerRepo}/commits?per_page={count}";
            var json = await _http.GetStringAsync(url);
            var array = JArray.Parse(json);

            var commits = new List<GitCommit>();
            foreach (var item in array)
            {
                var dateStr = item["commit"]?["author"]?["date"]?.ToString() ?? "";
                DateTime.TryParse(dateStr, out var date);

                var sha = item["sha"]?.ToString() ?? "";
                commits.Add(new GitCommit
                {
                    Sha = sha,
                    Message = (item["commit"]?["message"]?.ToString() ?? "").Split('\n')[0],
                    Author = item["commit"]?["author"]?["name"]?.ToString() ?? "",
                    Date = date.ToString("yyyy-MM-dd HH:mm"),
                    TimeAgo = GetTimeAgo(date),
                    Url = $"https://github.com/{ownerRepo}/commit/{sha}"
                });
            }
            return commits;
        }
        catch (Exception ex)
        {
            return new List<GitCommit>
            {
                new() { Sha = "ERROR0", Message = $"불러오기 실패: {ex.Message}", TimeAgo = "" }
            };
        }
    }

    private string GetTimeAgo(DateTime date)
    {
        var diff = DateTime.Now - date;
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}분 전";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}시간 전";
        return $"{(int)diff.TotalDays}일 전";
    }
}
