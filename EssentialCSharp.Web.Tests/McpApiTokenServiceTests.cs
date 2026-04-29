using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EssentialCSharp.Web.Tests;

[NotInParallel("McpTests")]
[ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerClass)]
public class McpApiTokenServiceTests(WebApplicationFactory factory)
{
    [Test]
    public async Task CreateTokenAsync_WithoutExpiry_UsesSixMonthDefault()
    {
        string userId = await McpTestHelper.CreateUserAsync(factory, "mcp-default-expiry");

        using var scope = factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<McpApiTokenService>();

        (_, var entity) = await tokenService.CreateTokenAsync(userId, "default-expiry");

        await Assert.That(entity.ExpiresAt).IsNotNull();
        await Assert.That(entity.ExpiresAt!.Value)
            .IsEqualTo(McpApiTokenService.GetDefaultExpirationUtc(entity.CreatedAt));
    }

    [Test]
    public async Task CreateTokenAsync_WithExpiryBeyondSixMonths_Throws()
    {
        string userId = await McpTestHelper.CreateUserAsync(factory, "mcp-max-expiry");

        using var scope = factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<McpApiTokenService>();
        DateTime requestedExpiry = McpApiTokenService.GetDefaultExpirationUtc(DateTime.UtcNow).AddDays(1);

        await Assert.That(() => tokenService.CreateTokenAsync(userId, "too-long", requestedExpiry))
            .Throws<ArgumentOutOfRangeException>()
            .WithMessageContaining("6 months");
    }
}
