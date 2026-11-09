using AqtCoreFW.SharedKernel.Api;
using AqtCoreFW.SharedKernel.Application.Pagination;

namespace AqtCoreFW.Regression.Tests.Smoke;

/// <summary>
/// Smoke tests — post-deploy sanity at domain level (TC-REG-SMOKE-001 to 005).
/// Run with: dotnet test --filter "Category=Smoke"
/// These verify core domain invariants are intact without needing infrastructure.
/// </summary>
public sealed class SmokeTestSuite
{
    [Fact]
    [Trait("Category", "Smoke")]
    public void ApiResponse_Ok_CanBeConstructed()
    {
        var response = ApiResponse<string>.Ok("healthy");

        response.Success.Should().BeTrue();
        response.Data.Should().Be("healthy");
        response.Meta.Should().NotBeNull();
        response.Meta!.RequestId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public void PagedResult_CanBeConstructed_WithPagination()
    {
        var meta = new PaginationMeta(1, 10, 5, null, null, true);
        var paged = new PagedResult<int>([1, 2, 3], 30, meta);

        paged.Items.Should().HaveCount(3);
        paged.TotalCount.Should().Be(30);
        paged.Meta.HasNextPage.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public void ApiError_CanBeConstructed_WithGovErrorCode()
    {
        var error = new ApiError("GOV_SYS_000", "System error");

        error.Code.Should().StartWith("GOV_");
        error.Message.Should().NotBeNullOrEmpty();
    }

}
