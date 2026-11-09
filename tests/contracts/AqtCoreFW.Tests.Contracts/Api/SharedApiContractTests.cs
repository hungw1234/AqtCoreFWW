using System.Text.Json;
using AqtCoreFW.SharedKernel.Api;
using AqtCoreFW.SharedKernel.Application.Pagination;

namespace AqtCoreFW.Tests.Contracts.Api;

/// <summary>
/// Contract tests for shared API envelope types — ApiResponse and PagedResult.
/// TC-CON-API-001, TC-CON-API-002.
/// </summary>
public sealed class SharedApiContractTests
{
    [Fact]
    [Trait("Category", "Contract")]
    public void ApiResponse_JsonShape_HasSuccessDataErrorsFields()
    {
        var response = ApiResponse<string>.Ok("hello");
        var json = JsonSerializer.Serialize(response);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("Success", out var success).Should().BeTrue();
        success.GetBoolean().Should().BeTrue();
        root.TryGetProperty("Data", out _).Should().BeTrue();
        root.TryGetProperty("Meta", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Contract")]
    public void ApiResponse_Fail_HasErrorsArray()
    {
        var errors = new List<FluentResults.Error> { new("Something went wrong") };
        var response = ApiResponse<string>.Fail(errors);
        var json = JsonSerializer.Serialize(response);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("Success", out var success).Should().BeTrue();
        success.GetBoolean().Should().BeFalse();
        root.TryGetProperty("Errors", out var errArr).Should().BeTrue();
        errArr.ValueKind.Should().Be(JsonValueKind.Array);
        errArr.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public void ApiError_RoundTrip_AllFieldsPreserved()
    {
        var error = new ApiError("GOV_CAS_001", "Not found", "Không tìm thấy", "trace-123", "CaseId");
        var json = JsonSerializer.Serialize(error);
        var d = JsonSerializer.Deserialize<ApiError>(json);

        d.Should().NotBeNull();
        d!.Code.Should().Be("GOV_CAS_001");
        d.Message.Should().Be("Not found");
        d.DetailVi.Should().Be("Không tìm thấy");
        d.TraceId.Should().Be("trace-123");
        d.Property.Should().Be("CaseId");
    }

    [Fact]
    [Trait("Category", "Contract")]
    public void PagedResult_JsonShape_HasItemsTotalCountMeta()
    {
        var items = new List<string> { "a", "b" };
        var meta = new PaginationMeta(1, 10, 5, null, null, true);
        var paged = new PagedResult<string>(items, 50, meta);
        var json = JsonSerializer.Serialize(paged);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("Items", out var data).Should().BeTrue();
        data.ValueKind.Should().Be(JsonValueKind.Array);
        data.GetArrayLength().Should().Be(2);
        root.TryGetProperty("TotalCount", out var tc).Should().BeTrue();
        tc.GetInt32().Should().Be(50);
        root.TryGetProperty("Meta", out var m).Should().BeTrue();
        m.TryGetProperty("Page", out _).Should().BeTrue();
        m.TryGetProperty("PageSize", out _).Should().BeTrue();
        m.TryGetProperty("HasNextPage", out _).Should().BeTrue();
    }
}
