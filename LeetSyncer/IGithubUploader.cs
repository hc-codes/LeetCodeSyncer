namespace LeetSyncer;

public interface IGithubUploader
{
    Task PushSolutionAsync(ProblemInfo problem, string solution, string language = "python");
}
