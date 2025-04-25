using System.Text.Encodings.Web;
using Octokit;

namespace LeetSyncer;

public class GitHubUploader : IGithubUploader
{
    private readonly GitHubClient _client;
    private readonly string _owner;
    private readonly string _repo;
    private readonly string _branch;

    public GitHubUploader(string token, string owner, string repo, string branch = "main")
    {
        _owner = owner;
        _repo = repo;
        _branch = branch;

        _client = new GitHubClient(new ProductHeaderValue("leetcode-uploader"))
        {
            Credentials = new Credentials(token)
        };
    }

    public async Task PushSolutionAsync(ProblemInfo problem, string solution, string language = "python")
    {
        Console.WriteLine($"Pushing solution for problem: {problem.Title}");

        string sanitizedTitle = $"{problem.Id:D4}-{Sanitize(problem.Title)}";
        string fileExtension = GetFileExtension(language);
        string folderPath = problem.Difficulty + "/" + sanitizedTitle;
        string solutionFilePath = $"{folderPath}/{sanitizedTitle}.{fileExtension}";
        string readmeFilePath = $"{folderPath}/README.md";

        string contentWithMeta = $"// Problem: {problem.Title}\n" +
                                 $"// Difficulty: {problem.Difficulty}\n" +
                                 $"// LeetCode URL: https://leetcode.com/problems/{problem.TitleSlug}/\n" +
                                 $"// Date: {problem.TimeStamp}\n\n" +
                                 solution;

        string markdownContent = $"# {problem.Title}\n\n" +
                                 $"**Difficulty**: {problem.Difficulty}\n\n" +
                                 $"**URL**: [https://leetcode.com/problems/{problem.TitleSlug}/](https://leetcode.com/problems/{problem.TitleSlug}/)\n\n" +
                                 $"---\n\n" +
                                 $"{problem.Question}\n";

        try
        {
            await PushFileAsync(solutionFilePath, contentWithMeta, $"Add/update solution for {problem.Title}");
            await PushFileAsync(readmeFilePath, markdownContent, $"Add/update README for {problem.Title}");

            Console.WriteLine("All push operations completed.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    private async Task PushFileAsync(string path, string content, string commitMessage)
    {
        string sha = null;
        try
        {
            var existing = await _client.Repository.Content.GetAllContentsByRef(_owner, _repo, path, _branch);
            var existingContent = existing[0].Content;
            if (existingContent == content)
            {
                Console.WriteLine($"No changes to push for: {path}");
                return;
            }
            sha = existing[0].Sha;
        }
        catch (NotFoundException) { }

        if (sha != null)
        {
            var update = new UpdateFileRequest(commitMessage, content, sha, _branch);
            await _client.Repository.Content.UpdateFile(_owner, _repo, path, update);
        }
        else
        {
            var create = new CreateFileRequest(commitMessage, content, _branch);
            await _client.Repository.Content.CreateFile(_owner, _repo, path, create);
        }

        Console.WriteLine($"Pushed: {path}");
    }

    private string Sanitize(string title) => string.Concat(title.Split(Path.GetInvalidFileNameChars()));

    private string GetFileExtension(string language)
    {
        return language.ToLower() switch
        {
            "cpp" or "c++" => "cpp",
            "java" => "java",
            "c#" or "csharp" => "cs",
            "python" => "py",
            "javascript" => "js",
            "typescript" => "ts",
            _ => "txt"
        };
    }
}
