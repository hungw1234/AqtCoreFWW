using System.Net;
using AqtCoreFW.Tests.Integration.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Files;

/// <summary>
/// Smoke tests for FilesController — /api/v1/files
/// Strategy: MinIO is NOT running in test env — test 404/auth only, skip upload.
/// Upload requires MinIO (IStorageProvider) → would throw 500 without container.
/// GET /api/v1/files/{id} with random Guid → 404 (file not in DB) is safe to test.
/// GET /api/v1/files/{id}/metadata → 404 likewise.
/// </summary>
[Collection("Integration")]
public class FilesApiTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private const string BaseUrl = "/api/v1/files";

    [Fact]
    public async Task GetFile_WithRandomGuid_Returns404()
    {
        // No MinIO needed — query hits DB first; record not found → 404
        var randomId = Guid.NewGuid();

        var response = await Client.GetAsync(
            $"{BaseUrl}/{randomId}?tenantId={Guid.NewGuid()}&requestedBy={Guid.NewGuid()}");

        // 404 = file not found in DB (expected). 500 would mean infrastructure failure.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetFileMetadata_WithRandomGuid_Returns404()
    {
        var randomId = Guid.NewGuid();

        var response = await Client.GetAsync(
            $"{BaseUrl}/{randomId}/metadata?tenantId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteFile_WithRandomGuid_Returns404()
    {
        var randomId = Guid.NewGuid();

        var response = await Client.DeleteAsync(
            $"{BaseUrl}/{randomId}?tenantId={Guid.NewGuid()}&deletedBy={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetFile_Unauthenticated_Returns401Or403()
    {
        var anonClient = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await anonClient.GetAsync(
            $"{BaseUrl}/{Guid.NewGuid()}?tenantId={Guid.NewGuid()}&requestedBy={Guid.NewGuid()}");

        // Auth enforcement — policy denies unauthenticated access
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }
}
