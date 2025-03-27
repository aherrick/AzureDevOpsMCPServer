using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol;
using ModelContextProtocol.Server;

var builder = Host.CreateEmptyApplicationBuilder(settings: null);

builder.Services.AddMcpServer().WithStdioServerTransport().WithTools();

var host = builder.Build();

// Get settings from environment variables
var azureDevOpsSettings = GetSettingsFromEnv();

// Initialize AzureDevOpsTools with settings from environment
AzureDevOpsTools.Initialize(azureDevOpsSettings);

await host.RunAsync();

// Method to get settings from environment variables
static AzureDevOpsSettings GetSettingsFromEnv()
{
    var settings = new AzureDevOpsSettings
    {
        Organization = Environment.GetEnvironmentVariable("AZURE_DEVOPS_ORG"),
        Project = Environment.GetEnvironmentVariable("AZURE_DEVOPS_PROJECT"),
        PersonalAccessToken = Environment.GetEnvironmentVariable("AZURE_DEVOPS_PAT"),
    };

    // Validate required parameters
    if (
        string.IsNullOrEmpty(settings.Organization)
        || string.IsNullOrEmpty(settings.Project)
        || string.IsNullOrEmpty(settings.PersonalAccessToken)
    )
    {
        throw new ArgumentException(
            "Missing required environment variables. Please set AZURE_DEVOPS_ORG, AZURE_DEVOPS_PROJECT, and AZURE_DEVOPS_PAT"
        );
    }

    return settings;
}

public class AzureDevOpsSettings
{
    public string Organization { get; set; }
    public string Project { get; set; }
    public string PersonalAccessToken { get; set; }
}

[McpToolType]
public static class AzureDevOpsTools
{
    private static HttpClient _httpClient;
    private static string _organization;
    private static string _project;

    public static void Initialize(AzureDevOpsSettings settings)
    {
        _organization = settings.Organization;
        _project = settings.Project;

        _httpClient = new HttpClient();
        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($":{settings.PersonalAccessToken}")
        );
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            credentials
        );
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json")
        );
    }

    [McpTool, Description("Retrieves work items based on a WIQL query for the project.")]
    public static async Task<string> ListWorkItemsByWiqlAsync(string wiqlQuery)
    {
        var url =
            $"https://dev.azure.com/{_organization}/{_project}/_apis/wit/wiql?api-version=7.1";

        // Create the WIQL request body
        var requestBody = new { query = wiqlQuery };
        var content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json"
        );

        var response = await _httpClient.PostAsync(url, content);
        response.EnsureSuccessStatusCode();

        // Get the initial response with work item IDs
        var jsonResponse = await response.Content.ReadAsStringAsync();

        // Parse the response to get work item IDs
        var jsonDoc = JsonDocument.Parse(jsonResponse);
        var workItemIds = jsonDoc
            .RootElement.GetProperty("workItems")
            .EnumerateArray()
            .Select(wi => wi.GetProperty("id").GetInt32())
            .ToList();

        if (workItemIds.Count == 0)
        {
            return "[]"; // Return empty array if no work items found
        }

        // Fetch detailed work item information
        var detailsUrl =
            $"https://dev.azure.com/{_organization}/{_project}/_apis/wit/workitems?ids={string.Join(",", workItemIds)}&api-version=7.1";
        var detailsResponse = await _httpClient.GetAsync(detailsUrl);
        detailsResponse.EnsureSuccessStatusCode();

        // Parse the detailed response and extract key fields
        var detailsJson = await detailsResponse.Content.ReadAsStringAsync();
        var detailsDoc = JsonDocument.Parse(detailsJson);

        var simplifiedItems = detailsDoc
            .RootElement.GetProperty("value")
            .EnumerateArray()
            .Select(item => new
            {
                id = item.GetProperty("id").GetInt32(),
                title = item.GetProperty("fields").GetProperty("System.Title").GetString(),
                state = item.GetProperty("fields").GetProperty("System.State").GetString(),
            })
            .ToList();

        // Serialize the simplified list to JSON
        return JsonSerializer.Serialize(simplifiedItems);
    }

    [McpTool, Description("Retrieves all pull requests across all repositories in the project.")]
    public static async Task<string> ListAllPullRequestsAsync()
    {
        // First, get all repositories
        var reposUrl =
            $"https://dev.azure.com/{_organization}/{_project}/_apis/git/repositories?api-version=7.1";
        var reposResponse = await _httpClient.GetAsync(reposUrl);
        reposResponse.EnsureSuccessStatusCode();

        var reposJson = await reposResponse.Content.ReadAsStringAsync();
        var reposDoc = JsonDocument.Parse(reposJson);

        var repoIds = reposDoc
            .RootElement.GetProperty("value")
            .EnumerateArray()
            .Select(repo => repo.GetProperty("id").GetString())
            .ToList();

        if (repoIds.Count == 0)
        {
            return "[]"; // Return empty array if no repositories found
        }

        // Create tasks to get pull requests for each repository concurrently
        var pullRequestTasks = repoIds.Select(async repoId =>
        {
            var prUrl =
                $"https://dev.azure.com/{_organization}/{_project}/_apis/git/repositories/{repoId}/pullrequests?api-version=7.1";
            var prResponse = await _httpClient.GetAsync(prUrl);
            prResponse.EnsureSuccessStatusCode();

            var prJson = await prResponse.Content.ReadAsStringAsync();
            var prDoc = JsonDocument.Parse(prJson);

            return prDoc
                .RootElement.GetProperty("value")
                .EnumerateArray()
                .Select(pr => new
                {
                    repositoryId = pr.GetProperty("repository").GetProperty("id").GetString(),
                    pullRequestId = pr.GetProperty("pullRequestId").GetInt32(),
                    title = pr.GetProperty("title").GetString(),
                    status = pr.GetProperty("status").GetString(),
                    createdBy = pr.GetProperty("createdBy").GetProperty("displayName").GetString(),
                    creationDate = pr.GetProperty("creationDate").GetString(),
                });
        });

        // Wait for all pull request fetches to complete
        var pullRequestResults = await Task.WhenAll(pullRequestTasks);

        // Combine all results into a single list
        var allPullRequests = pullRequestResults.SelectMany(result => result).ToList();

        // Serialize the combined list to JSON
        return JsonSerializer.Serialize(allPullRequests);
    }
}