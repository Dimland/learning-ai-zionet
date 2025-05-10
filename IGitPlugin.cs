using LibGit2Sharp;

public interface IGitPlugin
{
    IEnumerable<Commit> GetLatestCommits(string repoPath, int n);
}