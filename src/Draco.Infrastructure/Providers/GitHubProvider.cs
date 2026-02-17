using Draco.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Octokit;

namespace Draco.Infrastructure.Providers;

public class GitHubProvider : IGitProvider
{
    private readonly GitHubClient _client;
    private readonly ILogger<GitHubProvider> _logger;

    public GitHubProvider(ILogger<GitHubProvider> logger, IConfiguration configuration)
    {
        _logger = logger;
        var token = configuration["GitHub:Token"] ?? configuration["GITHUB_TOKEN"] ?? "token";
        _client = new GitHubClient(new ProductHeaderValue("Draco-Sentinel"))
        {
            Credentials = new Credentials(token)
        };
    }

    public async Task CreatePullRequestAsync(string repoOwner, string repoName, string branchName, string title, string body, IDictionary<string, string> files, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating Pull Request in {Owner}/{Repo}", repoOwner, repoName);

        try
        {
            // 1. Get reference to main branch
            var repo = await _client.Repository.Get(repoOwner, repoName);
            var mainBranch = await _client.Git.Reference.Get(repoOwner, repoName, $"heads/{repo.DefaultBranch}");

            // 2. Create a new branch
            await _client.Git.Reference.Create(repoOwner, repoName, new NewReference($"refs/heads/{branchName}", mainBranch.Object.Sha));

            // 3. Create commits for each file
            foreach (var file in files)
            {
                await _client.Repository.Content.CreateFile(repoOwner, repoName, file.Key, new CreateFileRequest($"Draco: {title}", file.Value, branchName));
            }

            // 4. Create the PR
            await _client.PullRequest.Create(repoOwner, repoName, new NewPullRequest(title, branchName, repo.DefaultBranch) { Body = body });
            
            _logger.LogInformation("Pull Request created successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Pull Request in GitHub.");
        }
    }
}
