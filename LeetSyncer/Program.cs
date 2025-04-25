using System.Text;
using System.Text.Json;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LeetSyncer;

class LeetCodeSyncer
{
    private static readonly HttpClient _client = new();

    // Configuration
    private static readonly string LeetCodeSessionCookie = Environment.GetEnvironmentVariable("LEETCODE_SESSION_COOKIE");
    private static readonly string LeetCodeCsrfToken = Environment.GetEnvironmentVariable("LEETCODE_CSRF_TOKEN");

    // GitHub configuration
    private static readonly string GitHubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
    private static readonly string RepoOwner = Environment.GetEnvironmentVariable("REPO_OWNER");
    private static readonly string RepoName = Environment.GetEnvironmentVariable("REPO_NAME");

    // Other settings
    private const string LeetCodeUri = "https://leetcode.com/graphql";

    private static IGithubUploader _ghe;

    static async Task Main(string[] args)
    {
        try
        {
            Console.WriteLine("Starting LeetCode to GitHub solution pusher...");

            // Get all solved problems from LeetCode
            var solvedProblems = await GetSolvedProblems();
            Console.WriteLine($"Found {solvedProblems.Count} solved problems");

            // For each problem, get the solution and push to GitHub
            foreach (var problem in solvedProblems)
            {
                try
                {
                    // Get the solution code for this problem
                    var submissionIdTask = GetAcceptedSubmissionId(problem.TitleSlug);
                    var fetchLeetCodeQuestionAsMarkdownTask = FetchLeetCodeQuestionAsMarkdown(problem.TitleSlug);
                    await Task.WhenAll(submissionIdTask, fetchLeetCodeQuestionAsMarkdownTask);
                    var (solution, timestamp) = await GetSolution(submissionIdTask.Result ?? 0);
                    if (string.IsNullOrEmpty(solution))
                    {
                        ConsoleExtension.WriteError($"No solution found for problem: {problem.Title}");
                        continue;
                    }

                    // Push the solution to GitHub
                    _ghe = new GitHubUploader(GitHubToken, RepoOwner, RepoName);
                    problem.Question = fetchLeetCodeQuestionAsMarkdownTask.Result;
                    problem.TimeStamp = DateTimeOffset.FromUnixTimeSeconds(timestamp).Date.ToString("yyyy-MM-dd");
                    await _ghe.PushSolutionAsync(problem, solution);
                }
                catch (Exception ex)
                {
                    ConsoleExtension.WriteError($"Error processing problem {problem.Title}: {ex.Message}");
                }
            }

            Console.WriteLine("All solutions have been pushed to GitHub successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }

    private static async Task<List<ProblemInfo>> GetSolvedProblems()
    {
        var problems = new List<ProblemInfo>();

        var query = @"
            query problemsetQuestionListV2(
              $filters: QuestionFilterInput,
              $searchKeyword: String,
              $skip: Int,
              $sortBy: QuestionSortByInput,
              $categorySlug: String
            ) {
              problemsetQuestionListV2(
                filters: $filters
                searchKeyword: $searchKeyword
                skip: $skip
                sortBy: $sortBy
                categorySlug: $categorySlug
              ) {
                questions {
                  id
                  titleSlug
                  title
                  translatedTitle
                  questionFrontendId
                  paidOnly
                  difficulty
                  topicTags {
                    name
                    slug
                    nameTranslated
                  }
                  status
                  isInMyFavorites
                  frequency
                  acRate
                }
                totalLength
                finishedLength
                hasMore
              }
            }";

        var variables = new
        {
            skip = 0,
            categorySlug = "all-code-essentials",
            filters = new
            {
                filterCombineType = "ALL",
                statusFilter = new { questionStatuses = new[] { "SOLVED" }, @operator = "IS" },
                difficultyFilter = new { difficulties = Array.Empty<string>(), @operator = "IS" },
                languageFilter = new { languageSlugs = Array.Empty<string>(), @operator = "IS" },
                topicFilter = new { topicSlugs = Array.Empty<string>(), @operator = "IS" },
                acceptanceFilter = new { },
                frequencyFilter = new { },
                lastSubmittedFilter = new { },
                publishedFilter = new { },
                companyFilter = new { companySlugs = Array.Empty<string>(), @operator = "IS" },
                positionFilter = new { positionSlugs = Array.Empty<string>(), @operator = "IS" },
                premiumFilter = new { premiumStatus = Array.Empty<string>(), @operator = "IS" }
            },
            searchKeyword = "",
            sortBy = new { sortField = "CUSTOM", sortOrder = "ASCENDING" }
        };

        var requestBody = new
        {
            query,
            variables,
            operationName = "problemsetQuestionListV2"
        };

        var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, LeetCodeUri);
        request.Headers.TryAddWithoutValidation("Cookie", LeetCodeSessionCookie);
        request.Headers.TryAddWithoutValidation("X-Csrftoken", LeetCodeCsrfToken);
        request.Content = content;

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var responseContent = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine("Response content:");
            Console.WriteLine(responseContent);
            throw new Exception($"LeetCode API request failed. Status: {response.StatusCode}");
        }

        var data = JObject.Parse(responseContent);
        var solvedList = data["data"]?["problemsetQuestionListV2"]?["questions"];
        if (solvedList == null)
        {
            throw new Exception("No solved problems found or response format is invalid.");
        }

        foreach (var item in solvedList)
        {
            problems.Add(new ProblemInfo
            {
                Title = item["title"]?.ToString() ?? throw new ArgumentNullException("Title"),
                TitleSlug = item["titleSlug"]?.ToString() ?? throw new ArgumentNullException("Title slug"),
                Difficulty = item["difficulty"]?.ToString() ?? throw new ArgumentNullException("Difficulty"),
                Id = item["id"]?.ToString() ?? throw new ArgumentNullException("Id")
            });
        }

        return problems;
    }

    private static async Task<int?> GetAcceptedSubmissionId(string titleSlug)
    {
        var listPayload = new
        {
            query = @"
                query submissionList($offset: Int!, $limit: Int!, $lastKey: String, $questionSlug: String!, $status: Int, $lang: Int) {
                  questionSubmissionList(offset: $offset, limit: $limit, lastKey: $lastKey, questionSlug: $questionSlug, status: $status, lang: $lang,) {
                    submissions {
                      id
                      timestamp
                      statusDisplay
                    }
                  }
                }",
            variables = new
            {
                offset = 0,
                limit = 1,
                lastKey = (string)null,
                questionSlug = titleSlug,
                status = 10
            },
            operationName = "submissionList"
        };

        var content = new StringContent(JsonConvert.SerializeObject(listPayload), Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, LeetCodeUri);
        request.Headers.TryAddWithoutValidation("Cookie", $"{LeetCodeSessionCookie}");
        request.Headers.TryAddWithoutValidation("X-CSRFToken", $"{LeetCodeCsrfToken}");
        request.Content = content;

        //using var client = new HttpClient();
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var data = JObject.Parse(json);

        var submissions = data["data"]?["questionSubmissionList"]?["submissions"] as JArray;
        var accepted = submissions?.FirstOrDefault(x => x["statusDisplay"]?.ToString() == "Accepted");

        return accepted?["id"]?.ToObject<int>();
    }

    private static async Task<string> FetchLeetCodeQuestionAsMarkdown(string slug)
    {
        var query = @"
                        query getQuestionDetail($titleSlug: String!) {
                            question(titleSlug: $titleSlug) {
                                title
                                difficulty
                                content
                                topicTags {
                                    name
                                }
                            }
                        }";

        var payload = new
        {
            query = query,
            variables = new { titleSlug = slug }
        };

        var json = JsonConvert.SerializeObject(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", $"https://leetcode.com/problems/{slug}/");

        var response = await _client.PostAsync(LeetCodeUri, content);
        var result = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(result);
        var question = doc.RootElement.GetProperty("data").GetProperty("question");

        var title = question.GetProperty("title").GetString();
        var difficulty = question.GetProperty("difficulty").GetString();
        var rawHtml = question.GetProperty("content").GetString();

        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(rawHtml);
        var plainText = htmlDoc.DocumentNode.InnerText;

        var tags = question.GetProperty("topicTags");
        var tagList = new StringBuilder();
        foreach (var tag in tags.EnumerateArray())
        {
            tagList.Append(tag.GetProperty("name").GetString() + ", ");
        }

        var markdown = new StringBuilder();
        markdown.AppendLine($"# {title}\n");
        markdown.AppendLine($"**Difficulty**: {difficulty}\n");
        markdown.AppendLine("**Tags**: " + tagList.ToString().TrimEnd(',', ' ') + "\n");
        markdown.AppendLine("---\n");
        markdown.AppendLine(plainText);

        return markdown.ToString();
    }

    private static async Task<(string, long)> GetSolution(int submissionId)
    {
        // Set up headers for LeetCode
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", $"LEETCODE_SESSION={LeetCodeSessionCookie}; csrftoken={LeetCodeCsrfToken}");
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://leetcode.com");
        _client.DefaultRequestHeaders.TryAddWithoutValidation("X-CSRFToken", LeetCodeCsrfToken);
        _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");

        var query = @"
                    query submissionDetails($submissionId: Int!) {
                      submissionDetails(submissionId: $submissionId) {
                        code
                        lang {
                          name
                        }
                        question {
                          titleSlug
                        }
                        timestamp
                      }
                    }";

        var variables = new { submissionId };

        var requestBody = new
        {
            query,
            variables,
            operationName = "submissionDetails"
        };

        var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

        var response = await _client.PostAsync(LeetCodeUri, content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        var data = JObject.Parse(responseJson);

        var submission = data["data"]?["submissionDetails"]?["code"] ?? throw new ArgumentNullException();
        var timestamp = data["data"]?["submissionDetails"]?["timestamp"]?.ToString() ?? throw new ArgumentOutOfRangeException();
        var date = long.Parse(timestamp);

        // Find the first accepted submission in the specified language
        var code = submission.ToString();
        return (code, date);
    }
}