using Microsoft.SemanticKernel;
using System.ComponentModel;
using LibGit2Sharp;
using System.Text;
using System.IO;
using System.Text.Json;

public class PromptPlugin
{
    private readonly string RELEASE_STORAGE_FILE = "release_history.json";

    [KernelFunction]
    [Description("Generates release notes based on Git commits")]
    public async Task<string> GenerateReleaseNotes(
        [Description("The Git repository path")] string repoPath,
        [Description("Number of commits to include in release notes")] int commitCount = 20,
        [Description("Release version (optional)")] string version = "",
        Kernel kernel)
    {
        if (string.IsNullOrEmpty(repoPath) || !Directory.Exists(repoPath) || !Directory.Exists(Path.Combine(repoPath, ".git")))
        {
            return "Error: Please provide a valid Git repository path.";
        }

        try
        {
            using var repo = new Repository(repoPath);
            var commits = repo.Commits.Take(commitCount).ToList();

            var commitMessages = new StringBuilder();
            foreach (var commit in commits)
            {
                commitMessages.AppendLine($"- {commit.MessageShort} (by {commit.Author.Name}, {commit.Author.When:yyyy-MM-dd})");
            }

            if (string.IsNullOrEmpty(version))
            {
                var versionFilePath = Path.Combine(repoPath, "version.json");
                if (File.Exists(versionFilePath))
                {
                    string json = File.ReadAllText(versionFilePath);
                    var semVersion = JsonSerializer.Deserialize<SemanticVersion>(json);
                    if (semVersion != null)
                    {
                        version = $"v{semVersion}";
                    }
                }

                if (string.IsNullOrEmpty(version))
                {
                    version = $"v{DateTime.Now:yyyy.MM.dd}";
                }
            }

            var prompt = @$"
As a release manager, generate comprehensive release notes for version {version} based on the following git commit messages.
Organize the changes into relevant categories such as:
- 🌟 New Features
- 🛠️ Improvements
- 🐛 Bug Fixes
- 🔧 Technical Updates
- 📚 Documentation

For each category, group related changes together and provide clear, concise descriptions.
Format the release notes in a professional and readable way with markdown formatting.
Do not include any information that is not directly derived from or reasonably inferred from the commit messages.

### Commit Messages:
{commitMessages}

### Release Notes:";

            // Execute the prompt with the kernel
            var result = await kernel.InvokePromptAsync(prompt);

            // Store the release in history
            StoreReleaseHistory(repoPath, version, result.ToString());

            return $"# Release Notes for {version}\n\n{result}";
        }
        catch (Exception ex)
        {
            return $"Error generating release notes: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("Gets history of previous releases")]
    public string GetReleaseHistory(
        [Description("The Git repository path")] string repoPath,
        [Description("Maximum number of releases to show")] int count = 5)
    {
        if (string.IsNullOrEmpty(repoPath) || !Directory.Exists(repoPath))
        {
            return "Error: Please provide a valid repository path.";
        }

        try
        {
            var storageFile = Path.Combine(repoPath, RELEASE_STORAGE_FILE);
            if (!File.Exists(storageFile))
            {
                return "No release history found.";
            }

            string json = File.ReadAllText(storageFile);
            var releases = JsonSerializer.Deserialize<List<ReleaseInfo>>(json) ?? new List<ReleaseInfo>();

            if (releases.Count == 0)
            {
                return "No release history found.";
            }

            var result = new StringBuilder("# Release History\n\n");
            foreach (var release in releases.OrderByDescending(r => r.Date).Take(count))
            {
                result.AppendLine($"## {release.Version} - {release.Date:yyyy-MM-dd}");
                result.AppendLine();
                result.AppendLine("Summary: " + release.Summary);
                result.AppendLine();
            }

            return result.ToString();
        }
        catch (Exception ex)
        {
            return $"Error retrieving release history: {ex.Message}";
        }
    }

    private void StoreReleaseHistory(string repoPath, string version, string releaseNotes)
    {
        try
        {
            var storageFile = Path.Combine(repoPath, RELEASE_STORAGE_FILE);
            var releases = new List<ReleaseInfo>();

            if (File.Exists(storageFile))
            {
                string json = File.ReadAllText(storageFile);
                releases = JsonSerializer.Deserialize<List<ReleaseInfo>>(json) ?? new List<ReleaseInfo>();
            }

            string summary = ExtractSummary(releaseNotes);

            var release = new ReleaseInfo
            {
                Version = version,
                Date = DateTime.Now,
                Notes = releaseNotes,
                Summary = summary
            };

            var existingRelease = releases.FirstOrDefault(r => r.Version == version);
            if (existingRelease != null)
            {
                releases.Remove(existingRelease);
            }

            releases.Add(release);

            string newJson = JsonSerializer.Serialize(releases, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(storageFile, newJson);
        }
        catch (Exception)
        {
            // todo Exception
        }
    }

    private string ExtractSummary(string releaseNotes)
    {
        var lines = releaseNotes.Split('\n');
        var summaryBuilder = new StringBuilder();

        int startLine = 0;
        while (startLine < lines.Length &&
               (string.IsNullOrWhiteSpace(lines[startLine]) ||
                lines[startLine].TrimStart().StartsWith("#")))
        {
            startLine++;
        }

        int lineCount = 0;
        for (int i = startLine; i < lines.Length && lineCount < 3; i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
            {
                summaryBuilder.AppendLine(lines[i].Trim());
                lineCount++;
            }
        }

        string summary = summaryBuilder.ToString().Trim();

        if (string.IsNullOrWhiteSpace(summary))
        {
            return "Release notes generated";
        }

        if (summary.Length > 200)
        {
            summary = summary.Substring(0, 197) + "...";
        }

        return summary;
    }
}

public class ReleaseInfo
{
    public string Version { get; set; } = "";
    public DateTime Date { get; set; }
    public string Notes { get; set; } = "";
    public string Summary { get; set; } = "";
}