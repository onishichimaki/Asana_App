using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskCapture.Api.Data;
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

    [Fact]
    public async Task FavoriteAndProjectRestriction_AreAppliedServerSide()
    {
        await using var factory = new TaskCaptureWebApplicationFactory();
        using var client = factory.CreateClient();
        var initial = await client.GetFromJsonAsync<AsanaProjectCatalog>("/api/asana/projects");
        Assert.NotNull(initial);
        var allowedProject = initial.Projects[1];
        var blockedProject = initial.Projects[0];

        var favoriteResponse = await client.PutAsJsonAsync(
            $"/api/asana/projects/{allowedProject.Gid}/favorite",
            new { isFavorite = true, projectName = allowedProject.Name });
        Assert.True(
            favoriteResponse.StatusCode == HttpStatusCode.NoContent,
            await favoriteResponse.Content.ReadAsStringAsync());

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskCaptureDbContext>();
            var user = await db.Users.SingleAsync();
            user.RestrictProjects = true;
            await db.SaveChangesAsync();
        }

        var restricted = await client.GetFromJsonAsync<AsanaProjectCatalog>("/api/asana/projects");
        Assert.NotNull(restricted);
        var onlyProject = Assert.Single(restricted.Projects);
        Assert.Equal(allowedProject.Gid, onlyProject.Gid);
        Assert.True(onlyProject.IsFavorite);

        var blocked = await client.GetAsync(
            $"/api/asana/projects/{blockedProject.Gid}/sections");
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
    }

    [Fact]
    public async Task MockConnectionStatus_IsReadyWithoutExternalCredentials()
    {
        await using var factory = new TaskCaptureWebApplicationFactory();
        using var client = factory.CreateClient();

        var status = await client.GetFromJsonAsync<AsanaConnectionStatus>(
            "/api/asana/connection");

        Assert.NotNull(status);
        Assert.True(status.Connected);
        Assert.Equal("Mock", status.CredentialMode);
    }
}
