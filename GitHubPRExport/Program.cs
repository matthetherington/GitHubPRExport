using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

if (args.Any(argument => argument is "--help" or "-h"))
{
    PrintUsage();
    return 0;
}

if (args.Length < 2)
{
    PrintUsage();
    return 1;
}

var owner = args[0].Trim();
var repository = args[1].Trim();
string? outputArgument = null;
string? author = null;

for (var index = 2; index < args.Length; index++)
{
    if (args[index] == "--author")
    {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
        {
            Console.Error.WriteLine("--author requires a GitHub username.");
            return 1;
        }

        author = args[index].Trim();
    }
    else if (args[index].StartsWith('-'))
    {
        Console.Error.WriteLine($"Unknown option: {args[index]}");
        return 1;
    }
    else if (outputArgument is null)
    {
        outputArgument = args[index];
    }
    else
    {
        Console.Error.WriteLine("Only one output path can be specified.");
        return 1;
    }
}

var outputPath = Path.GetFullPath(
    outputArgument ?? $"{owner}-{repository}-pull-requests.csv");

if (owner.Length == 0 || repository.Length == 0 || owner.Contains('/') || repository.Contains('/'))
{
    Console.Error.WriteLine("Owner and repository must be non-empty names, not a GitHub URL.");
    return 1;
}

var configuration = new ConfigurationBuilder()
    .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
    .Build();
var token = configuration["GitHub:Token"];

using var httpClient = new HttpClient
{
    BaseAddress = new Uri("https://api.github.com/")
};
httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("GitHubPRExport/1.0");
httpClient.DefaultRequestHeaders.Accept.Add(
    new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

if (!string.IsNullOrWhiteSpace(token))
{
    httpClient.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", token.Trim());
}

try
{
    var pullRequests = await GetAllPullRequestsAsync(httpClient, owner, repository);
    if (author is not null)
    {
        pullRequests = pullRequests
            .Where(pullRequest => string.Equals(
                pullRequest.User?.Login,
                author,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    await PopulateChangeStatisticsAsync(httpClient, owner, repository, pullRequests);

    var outputDirectory = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrEmpty(outputDirectory))
    {
        Directory.CreateDirectory(outputDirectory);
    }

    await using var writer = new StreamWriter(
        outputPath,
        append: false,
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

    await writer.WriteLineAsync("Title,Author,Date,Files Changed,Lines Added,Lines Removed,Description");
    foreach (var pullRequest in pullRequests)
    {
        await writer.WriteLineAsync(string.Join(',',
            Csv(pullRequest.Title),
            Csv(pullRequest.User?.Login ?? string.Empty),
            Csv(pullRequest.CreatedAt.ToString("O")),
            Csv(pullRequest.ChangedFiles.ToString()),
            Csv(pullRequest.Additions.ToString()),
            Csv(pullRequest.Deletions.ToString()),
            Csv(pullRequest.Body ?? string.Empty)));
    }

    var filterDescription = author is null ? string.Empty : $" by {author}";
    Console.WriteLine($"Exported {pullRequests.Count} pull requests{filterDescription} to {outputPath}");
    return 0;
}
catch (GitHubApiException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}
catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"Export failed: {exception.Message}");
    return 3;
}

static async Task<List<PullRequest>> GetAllPullRequestsAsync(
    HttpClient httpClient,
    string owner,
    string repository)
{
    const int pageSize = 100;
    var pullRequests = new List<PullRequest>();

    for (var page = 1; ; page++)
    {
        var path = $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}" +
                   $"/pulls?state=all&sort=created&direction=desc&per_page={pageSize}&page={page}";
        using var response = await httpClient.GetAsync(path);

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateGitHubApiExceptionAsync(response, $"{owner}/{repository}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        var pageItems = await JsonSerializer.DeserializeAsync<List<PullRequest>>(stream) ?? [];
        pullRequests.AddRange(pageItems);

        if (pageItems.Count < pageSize)
        {
            return pullRequests;
        }
    }
}

static async Task PopulateChangeStatisticsAsync(
    HttpClient httpClient,
    string owner,
    string repository,
    List<PullRequest> pullRequests)
{
    for (var index = 0; index < pullRequests.Count; index++)
    {
        var pullRequest = pullRequests[index];
        var path = $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}" +
                   $"/pulls/{pullRequest.Number}";
        using var response = await httpClient.GetAsync(path);

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateGitHubApiExceptionAsync(
                response,
                $"{owner}/{repository} pull request #{pullRequest.Number}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        var statistics = await JsonSerializer.DeserializeAsync<PullRequestStatistics>(stream)
                         ?? throw new GitHubApiException(
                             $"GitHub returned an empty response for pull request #{pullRequest.Number}.");

        pullRequests[index] = pullRequest with
        {
            ChangedFiles = statistics.ChangedFiles,
            Additions = statistics.Additions,
            Deletions = statistics.Deletions
        };
    }
}

static async Task<GitHubApiException> CreateGitHubApiExceptionAsync(
    HttpResponseMessage response,
    string resource)
{
    var responseBody = await response.Content.ReadAsStringAsync();
    var detail = TryReadErrorMessage(responseBody);
    var rateLimit = response.Headers.TryGetValues("X-RateLimit-Remaining", out var values) &&
                    values.FirstOrDefault() == "0"
        ? " The GitHub API rate limit has been exhausted; configure GitHub:Token or try again later."
        : string.Empty;

    return new GitHubApiException(
        $"GitHub returned {(int)response.StatusCode} ({response.ReasonPhrase}) for " +
        $"{resource}: {detail}{rateLimit}");
}

static string TryReadErrorMessage(string responseBody)
{
    try
    {
        return JsonSerializer.Deserialize<GitHubError>(responseBody)?.Message ?? "Unknown API error";
    }
    catch (JsonException)
    {
        return "Unknown API error";
    }
}

static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

static void PrintUsage()
{
    Console.WriteLine(
        "Usage: dotnet run --project GitHubPRExport -- <owner> <repository> [output.csv] [--author <username>]");
    Console.WriteLine(
        "Example: dotnet run --project GitHubPRExport -- dotnet runtime runtime-prs.csv --author github-actions");
}

internal sealed record PullRequest(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("user")] GitHubUser? User,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("body")] string? Body)
{
    public int ChangedFiles { get; init; }

    public int Additions { get; init; }

    public int Deletions { get; init; }
}

internal sealed record PullRequestStatistics(
    [property: JsonPropertyName("changed_files")] int ChangedFiles,
    [property: JsonPropertyName("additions")] int Additions,
    [property: JsonPropertyName("deletions")] int Deletions);

internal sealed record GitHubUser(
    [property: JsonPropertyName("login")] string Login);

internal sealed record GitHubError(
    [property: JsonPropertyName("message")] string Message);

internal sealed class GitHubApiException(string message) : Exception(message);
