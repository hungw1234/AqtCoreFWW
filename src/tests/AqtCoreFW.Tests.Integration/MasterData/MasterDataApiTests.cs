using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AqtCoreFW.Tests.Integration.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Tests.Integration.MasterData;

/// <summary>
/// Integration tests for MasterDataController — /api/v1/masterdata
/// Provinces/Districts/Wards are [AllowAnonymous].
/// CaseTypes/JobTitles require [Authorize] (any authenticated user).
/// </summary>
[Collection("Integration")]
public class MasterDataApiTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private const string BaseUrl = "/api/v1/masterdata";

    // --- Provinces (AllowAnonymous) ---

    [Fact]
    public async Task GetProvinces_Unauthenticated_Returns200()
    {
        // Provinces are [AllowAnonymous] — no auth required
        var anonClient = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await anonClient.GetAsync($"{BaseUrl}/provinces");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetProvinces_AsAuthenticatedUser_Returns200()
    {
        var response = await Client.GetAsync($"{BaseUrl}/provinces");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetProvinces_ResponseBody_IsValidApiResponseEnvelope()
    {
        var response = await Client.GetAsync($"{BaseUrl}/provinces");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // ApiResponse<T> envelope has "data" property
        body.TryGetProperty("data", out _).Should().BeTrue("response must be wrapped in ApiResponse envelope");
    }

    // --- Districts (AllowAnonymous) ---

    [Fact]
    public async Task GetDistricts_ByProvinceCode_Returns200()
    {
        // Province code "01" is Hanoi — seeded by MasterDataSeeder
        var response = await Client.GetAsync($"{BaseUrl}/provinces/01/districts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetDistricts_Unauthenticated_Returns200()
    {
        var anonClient = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await anonClient.GetAsync($"{BaseUrl}/provinces/01/districts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetDistricts_ForUnknownProvince_Returns200WithEmptyData()
    {
        // Non-existent province — controller returns empty list, not 404
        var response = await Client.GetAsync($"{BaseUrl}/provinces/ZZ_NONEXISTENT/districts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("data", out var data).Should().BeTrue();
        data.GetArrayLength().Should().Be(0);
    }

    // --- Wards (AllowAnonymous) ---

    [Fact]
    public async Task GetWards_ByProvinceAndDistrictCode_Returns200()
    {
        var response = await Client.GetAsync($"{BaseUrl}/provinces/01/districts/001/wards");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // --- CaseTypes (requires Authorize) ---

    [Fact]
    public async Task GetCaseTypes_AsAuthenticatedUser_Returns200()
    {
        var response = await Client.GetAsync($"{BaseUrl}/case-types");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetCaseTypes_Unauthenticated_Returns401Or403()
    {
        // CaseTypes has class-level [Authorize] and no [AllowAnonymous] override
        var anonClient = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await anonClient.GetAsync($"{BaseUrl}/case-types");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    // --- JobTitles (requires Authorize) ---

    [Fact]
    public async Task GetJobTitles_AsAuthenticatedUser_Returns200()
    {
        var response = await Client.GetAsync($"{BaseUrl}/job-titles");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // --- AdminUnits (AllowAnonymous) ---

    [Fact]
    public async Task GetAdminUnits_Returns200()
    {
        var response = await Client.GetAsync($"{BaseUrl}/admin-units");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
