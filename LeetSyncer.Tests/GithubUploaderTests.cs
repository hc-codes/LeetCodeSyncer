using System;
using NUnit.Framework;
using Octokit;
using Moq;

namespace LeetSyncer.Tests;

[TestFixture]
public class GitHubUploaderTests
{
    private Mock<IGitHubClient> _mockClient;
    private Mock<IRepositoryContentsClient> _mockContentsClient;
    private IGithubUploader _uploader = new GitHubUploader("token", "owner", "repo");
    private ProblemInfo _sampleProblem;

    [SetUp]
    public void Setup()
    {
        // Set up mocks
        _mockClient = new Mock<IGitHubClient>(MockBehavior.Strict);
        _mockContentsClient = new Mock<IRepositoryContentsClient>(MockBehavior.Strict);

        // Setup the contents client mock
        _mockClient.Setup(c => c.Repository.Content).Returns(_mockContentsClient.Object);

        // Sample problem for testing
        _sampleProblem = new ProblemInfo
        {
            Id = "42",
            Title = "Two Sum",
            TitleSlug = "two-sum",
            Difficulty = "Easy",
            Question = "Given an array of integers nums and an integer target...",
            TimeStamp = DateTime.Now.ToString("yyyy-MM-dd")
        };
    }

    [Test]
    public void Constructor_InitializesPropertiesCorrectly()
    {
        // Act
        var uploader = new GitHubUploader("token", "testOwner", "testRepo", "develop");

        // Assert - Test via reflection since properties are private
        var type = uploader.GetType();
        Assert.AreEqual("testOwner",
            type.GetField("_owner", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .GetValue(uploader));
        Assert.AreEqual("testRepo",
            type.GetField("_repo", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .GetValue(uploader));
        Assert.AreEqual("develop",
            type.GetField("_branch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .GetValue(uploader));
    }

    [Test]
    public void Constructor_WithDefaultBranch_UseMain()
    {
        // Act
        var uploader = new GitHubUploader("token", "testOwner", "testRepo");

        // Assert
        var type = uploader.GetType();
        Assert.AreEqual("main",
            type.GetField("_branch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .GetValue(uploader));
    }
}