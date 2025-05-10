using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile("appsettings.Development.json", optional: false, reloadOnChange: true)
    .Build();

var modelName = configuration["ModelName"] ?? throw new ApplicationException("ModelName not found");
var endpoint = configuration["Endpoint"] ?? throw new ApplicationException("Endpoint not found");
var apiKey = configuration["ApiKey"] ?? throw new ApplicationException("ApiKey not found");


Globals.Pat = configuration["Pat"]
             ?? throw new ApplicationException("Pat not found");

var builder = Kernel.CreateBuilder()
    .AddAzureOpenAIChatCompletion(modelName, endpoint, apiKey);


var kernel = builder.Build();
kernel.Plugins.AddFromType<GitPlugin>("git");

kernel.Plugins.AddFromType<PromptPlugin>("prompt");

// Add system prompt to improve output
string systemPrompt = @"
You are a helpful AI assistant specialized in working with Git repositories.
If a user asks you about commits or repository information, use the git plugin functions.
If the user asks you to generate release notes, use the prompt plugin.
If you don't have the plugin to perform a user request, explain what capabilities you have and suggest using available plugins.";

AzureOpenAIPromptExecutionSettings openAiPromptExecutionSettings = new()
{
    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
    Temperature = 0.3,
    MaxTokens = 2000
};

var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

var history = new ChatHistory();
history.AddSystemMessage(systemPrompt);


Console.WriteLine("Type 'exit' to quit, 'help' for a list of commands");
Console.WriteLine("\nExample commands:");
Console.WriteLine("- 'set repository path to C:\\path\\to\\repo'");
Console.WriteLine("- 'show me the latest 5 commits'");
Console.WriteLine("- 'generate release notes from the latest 20 commits'");
Console.WriteLine("- 'pull latest changes'");
Console.WriteLine("- 'update version to next minor release'");

string? currentRepositoryPath = null;

bool debugMode = true;

do
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("Me > ");
    Console.ResetColor();

    var userInput = Console.ReadLine();

    if (string.IsNullOrEmpty(userInput) || userInput.ToLower() == "exit")
    {
        break;
    }

    if (userInput.ToLower() == "help")
    {
        DisplayHelp();
        continue;
    }

    if (userInput.ToLower() == "debug on")
    {
        debugMode = true;
        Console.WriteLine("Debug mode turned ON");
        continue;
    }

    if (userInput.ToLower() == "debug off")
    {
        debugMode = false;
        Console.WriteLine("Debug mode turned OFF");
        continue;
    }

    if (debugMode)
    {
        Console.WriteLine($"User input: {userInput}");
    }

    history.AddUserMessage(userInput);

    if (userInput.Contains("repository path") && userInput.Contains("set"))
    {
        // Try to extract path
        var pathMatch = System.Text.RegularExpressions.Regex.Match(userInput, @"(?:to|as)\s+(?<path>.*?)$");
        if (pathMatch.Success)
        {
            currentRepositoryPath = pathMatch.Groups["path"].Value.Trim();

            if (debugMode)
            {
                Console.WriteLine($"Detected repository path: {currentRepositoryPath}");
            }
        }
    }

    Console.ForegroundColor = ConsoleColor.Green;
    Console.Write("Agent > ");
    Console.ResetColor();

    var fullResponse = "";

    try
    {
        var streamingResponse = chatCompletionService.GetStreamingChatMessageContentsAsync(
            history,
            openAiPromptExecutionSettings,
            kernel);

        await foreach (var chunk in streamingResponse)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write(chunk.Content);
            Console.ResetColor();
            fullResponse += chunk.Content;


        }

        if (debugMode && !string.IsNullOrEmpty(currentRepositoryPath))
        {
            try
            {
                if (Directory.Exists(currentRepositoryPath) &&
                    Directory.Exists(Path.Combine(currentRepositoryPath, ".git")))
                {
                    using var repo = new LibGit2Sharp.Repository(currentRepositoryPath);
                    Console.WriteLine($"Repository info - Current branch: {repo.Head.FriendlyName}");
                    Console.WriteLine($"Repository info - Last commit: {repo.Head.Tip.MessageShort}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error accessing repository: {ex.Message}");
            }
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Error: {ex.Message}");
        Console.ResetColor();

        if (debugMode)
        {
            Console.WriteLine(ex + " Error in processing request");
        }

        fullResponse = $"I encountered an error: {ex.Message}";
    }

    Console.WriteLine();
    history.AddMessage(AuthorRole.Assistant, fullResponse);
} while (true);



void DisplayHelp()
{
    Console.WriteLine("                      Available Commands");
    Console.WriteLine("Repository Management:");
    Console.WriteLine("  - set repository path to <path>");
    Console.WriteLine("  - show current branch");
    Console.WriteLine("  - pull latest changes");
    Console.WriteLine("  - commit changes with message \"<message>\"");
    Console.WriteLine("  - push changes to remote");

    Console.WriteLine("\nCommit Information:");
    Console.WriteLine("  - show [n] latest commits");
    Console.WriteLine("  - find commits with \"<pattern>\"");
    Console.WriteLine("  - compare commits <sha1> and <sha2>");

    Console.WriteLine("\nVersioning & Release Notes:");
    Console.WriteLine("  - show current version");
    Console.WriteLine("  - increment [major|minor|patch] version");
    Console.WriteLine("  - generate release notes");
    Console.WriteLine("  - show release history");

    Console.WriteLine("\nDebug Commands:");
}
public static class Globals
{
    public static string Pat { get; set; }
}