namespace Draco.Application.Interfaces;

public interface IGitProvider
{
    Task CreatePullRequestAsync(string repoOwner, string repoName, string branchName, string title, string body, IDictionary<string, string> files, CancellationToken cancellationToken = default);
}
