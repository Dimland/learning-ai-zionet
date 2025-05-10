using Microsoft.SemanticKernel;
using LibGit2Sharp;
using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using LibGit2Sharp.Handlers;

public class GitPlugin
{
    private string _repositoryPath = string.Empty;
    private const string VERSION_FILE = "version.json";

    [KernelFunction]
    [Description("Sets the repository path to work with")]
    public string SetRepositoryPath(
        [Description("Path to the Git repository")] string repositoryPath)
    {
        if (!Directory.Exists(repositoryPath))
        {
            return $"Error: Directory not found: {repositoryPath}";
        }

        if (!Directory.Exists(Path.Combine(repositoryPath, ".git")))
        {
            return $"Error: Not a valid Git repository: {repositoryPath}";
        }

        _repositoryPath = repositoryPath;
        return $"Repository path set to: {repositoryPath}";
    }

    [KernelFunction]
    [Description("Gets the latest commits from a Git repository")]
    public string GetLatestCommits(
        [Description("Number of commits to retrieve")] int countCommit = 5)
    {
        if (string.IsNullOrEmpty(_repositoryPath))
        {
            return "Error: Repository path is not set. Please set repository path first.";
        }

        try
        {
            using var repo = new Repository(_repositoryPath);
            var commits = repo.Commits.Take(countCommit).ToList();

            return FormatCommits(commits);
        }
        catch (Exception ex)
        {
            return $"Error accessing repository: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Gets the current branch")]
    public string GetCurrentBranch()
    {
        if (string.IsNullOrEmpty(_repositoryPath))
        {
            return "Error: Repository path is not set. Please set repository path first.";
        }

        try
        {
            using var repo = new Repository(_repositoryPath);
            return $"Current branch: {repo.Head.FriendlyName}";
        }
        catch (Exception ex)
        {
            return $"Error getting current branch: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Pulls latest changes from remote repository")]
    public string PullChanges(
        [Description("Name of remote to pull from (default: origin)")] string remote = "origin")
    {
        if (string.IsNullOrEmpty(_repositoryPath))
        {
            return "Error: Repository path is not set. Please set repository path first.";
        }

        try
        {
            using var repo = new Repository(_repositoryPath);

            // Get the remote
            var gitRemote = repo.Network.Remotes[remote];
            if (gitRemote == null)
            {
                return $"Error: Remote '{remote}' not found.";
            }

            // Fetch
            var refSpecs = gitRemote.FetchRefSpecs.Select(x => x.Specification);
            Commands.Fetch(repo, gitRemote.Name, refSpecs, null, "Fetching latest changes");

            // Get current branch
            var branch = repo.Head;
            if (branch.RemoteName == null)
            {
                return "Error: Current branch does not track a remote branch.";
            }

            // Merge
            var remoteBranchRef = repo.Branches[$"{remote}/{branch.FriendlyName}"];
            if (remoteBranchRef == null)
            {
                return $"Error: Remote branch '{remote}/{branch.FriendlyName}' not found.";
            }

            MergeResult mergeResult = repo.Merge(remoteBranchRef, new Signature("SK Git Plugin", "sk-git@example.com", DateTimeOffset.Now));

            return $"Pull result: {mergeResult.Status}";
        }
        catch (Exception ex)
        {
            return $"Error pulling changes: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Commits changes to the repository")]
    public string CommitChanges(
        [Description("Commit message")] string message)
    {
        if (string.IsNullOrEmpty(_repositoryPath))
        {
            return "Error: Repository path is not set. Please set repository path first.";
        }

        try
        {
            using var repo = new Repository(_repositoryPath);

            Commands.Stage(repo, "*");

            var status = repo.RetrieveStatus();
            if (!status.IsDirty)
            {
                return "Nothing to commit. Working directory clean.";
            }

            var signature = new Signature("SK Git Plugin", "sk-git@example.com", DateTimeOffset.Now);

            Commit commit = repo.Commit(message, signature, signature);

            return $"Changes committed successfully. Commit SHA: {commit.Sha}";
        }
        catch (Exception ex)
        {
            return $"Error committing changes: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Pushes commits to the remote repository")]
    public string PushChanges(
        [Description("Name of remote to push to (default: origin)")] string remote = "origin",
        [Description("Name of branch to push (default: current branch)")] string branch = "")
    {
        if (string.IsNullOrEmpty(_repositoryPath))
        {
            return "Error: Repository path is not set. Please set repository path first.";
        }

        try
        {
            using var repo = new Repository(_repositoryPath);

            if (string.IsNullOrEmpty(branch))
            {
                branch = repo.Head.FriendlyName;
            }

            var localBranch = repo.Branches[branch];
            if (localBranch == null)
            {
                return $"Error: Branch '{branch}' not found.";
            } 

            var origin = repo.Network.Remotes["origin"];
            var spec = $"refs/heads/{branch}:refs/heads/{branch}";
            var pat = Globals.Pat;
            var options = new PushOptions
            {
                CredentialsProvider = (_url, _user, _types) =>
                    new UsernamePasswordCredentials
                    {
                        Username = "x-access-token",
                        Password = pat
                    }
            };

            repo.Network.Push(origin, spec, options);

            return $"Successfully pushed '{branch}' to '{remote}'.";
        }
        catch (Exception ex)
        {
            return $"Error pushing changes: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Finds commits matching a specific pattern in the message")]
    public string FindCommits(
        [Description("Pattern to search for in commit messages")] string pattern,
        [Description("Maximum number of commits to search through")] int maxCount = 100)
    {
        if (string.IsNullOrEmpty(_repositoryPath))
        {
            return "Error: Repository path is not set. Please set repository path first.";
        }

        try
        {
            using var repo = new Repository(_repositoryPath);

            var matchingCommits = new List<Commit>();
            var regex = new Regex(pattern, RegexOptions.IgnoreCase);

            foreach (var commit in repo.Commits.Take(maxCount))
            {
                if (regex.IsMatch(commit.Message))
                {
                    matchingCommits.Add(commit);
                }
            }

            if (matchingCommits.Count == 0)
            {
                return $"No commits found matching pattern: {pattern}";
            }

            return $"Found {matchingCommits.Count} commits matching pattern '{pattern}':\n\n{FormatCommits(matchingCommits)}";
        }
        catch (Exception ex)
        {
            return $"Error finding commits: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Compares two commits and shows differences")]
    public string CompareCommits(
        [Description("SHA or reference of the first commit")] string commitSha1,
        [Description("SHA or reference of the second commit")] string commitSha2)
    {
        if (string.IsNullOrEmpty(_repositoryPath))
        {
            return "Error: Repository path is not set. Please set repository path first.";
        }

        try
        {
            using var repo = new Repository(_repositoryPath);

            var commit1 = repo.Lookup<Commit>(commitSha1);
            if (commit1 == null)
            {
                return $"Error: Commit '{commitSha1}' not found.";
            }

            var commit2 = repo.Lookup<Commit>(commitSha2);
            if (commit2 == null)
            {
                return $"Error: Commit '{commitSha2}' not found.";
            }

            var changes = repo.Diff.Compare<TreeChanges>(commit1.Tree, commit2.Tree);

            var result = new System.Text.StringBuilder();
            result.AppendLine($"Comparing {commitSha1} with {commitSha2}:");
            result.AppendLine($"Files added: {changes.Added.Count()}");
            result.AppendLine($"Files modified: {changes.Modified.Count()}");
            result.AppendLine($"Files deleted: {changes.Deleted.Count()}");
            result.AppendLine($"Files renamed: {changes.Renamed.Count()}");

            if (changes.Added.Any())
            {
                result.AppendLine("\nAdded files:");
                foreach (var change in changes.Added)
                {
                    result.AppendLine($"  + {change.Path}");
                }
            }

            if (changes.Modified.Any())
            {
                result.AppendLine("\nModified files:");
                foreach (var change in changes.Modified)
                {
                    result.AppendLine($"  * {change.Path}");
                }
            }

            if (changes.Deleted.Any())
            {
                result.AppendLine("\nDeleted files:");
                foreach (var change in changes.Deleted)
                {
                    result.AppendLine($"  - {change.Path}");
                }
            }

            return result.ToString();
        }
        catch (Exception ex)
        {
            return $"Error comparing commits: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Gets or updates the semantic version for the repository")]
    public string ManageSemanticVersion(
        [Description("Action to perform: get, increment-major, increment-minor, increment-patch")] string action = "get")
    {
        if (string.IsNullOrEmpty(_repositoryPath))
        {
            return "Error: Repository path is not set. Please set repository path first.";
        }

        try
        {
            var versionFilePath = Path.Combine(_repositoryPath, VERSION_FILE);
            var version = new SemanticVersion(1, 0, 0);

            if (File.Exists(versionFilePath))
            {
                string json = File.ReadAllText(versionFilePath);
                version = JsonSerializer.Deserialize<SemanticVersion>(json) ?? version;
            }

            switch (action.ToLower())
            {
                case "get":
                    return $"Current version: {version}";

                case "increment-major":
                    version = new SemanticVersion(version.Major + 1, 0, 0);
                    break;

                case "increment-minor":
                    version = new SemanticVersion(version.Major, version.Minor + 1, 0);
                    break;

                case "increment-patch":
                    version = new SemanticVersion(version.Major, version.Minor, version.Patch + 1);
                    break;

                default:
                    return $"Error: Unknown action '{action}'. Valid actions are: get, increment-major, increment-minor, increment-patch";
            }

            string newJson = JsonSerializer.Serialize(version);
            File.WriteAllText(versionFilePath, newJson);

            return $"Version updated to {version}";
        }
        catch (Exception ex)
        {
            return $"Error managing semantic version: {ex.Message}";
        }
    }

    private string FormatCommits(IEnumerable<Commit> commits)
    {
        var result = new System.Text.StringBuilder();
        foreach (var commit in commits)
        {
            result.AppendLine($"Commit: {commit.Sha[..7]}");
            result.AppendLine($"Author: {commit.Author.Name} <{commit.Author.Email}>");
            result.AppendLine($"Date: {commit.Author.When}");
            result.AppendLine($"Message: {commit.MessageShort}");
            result.AppendLine();
        }
        return result.ToString();
    }
}

public class SemanticVersion
{
    public int Major { get; set; }
    public int Minor { get; set; }
    public int Patch { get; set; }

    public SemanticVersion() { }

    public SemanticVersion(int major, int minor, int patch)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
    }

    public override string ToString()
    {
        return $"{Major}.{Minor}.{Patch}";
    }
}