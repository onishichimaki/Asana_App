using System.Net;
using System.Net.Http.Json;
using TaskCapture.Api.Services;

namespace TaskCapture.Api.Tests;

public sealed class AsanaMetadataApiTests
{
    [Fact]
    public async Task MockMode_ReturnsSelectableProjectsAndSections()
    {
        await using var factory = new TaskCaptureWebApplicationFactory();
        using var client = factory.CreateClient();

        var projects = await client.GetFromJsonAsync<AsanaProjectCatalog>("/api/asana/projects");

        Assert.NotNull(projects);
        Assert.NotNull(projects.DefaultProjectGid);
        Assert.NotEmpty(projects.Projects);
        var project = projects.Projects[0];

        var sections = await client.GetFromJsonAsync<IReadOnlyList<AsanaSectionOption>>(
            $"/api/asana/projects/{project.Gid}/sections");

        Assert.NotNull(sections);
        Assert.NotEmpty(sections);
    }

    [Fact]
    public async Task Sections_RejectsNonNumericProjectGid()
    {
        await using var factory = new TaskCaptureWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/asana/projects/not-a-gid/sections");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
