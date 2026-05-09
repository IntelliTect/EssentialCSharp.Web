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
    public async Task CreateTokenAsync_WithExpiryWithinSixMonths_UsesRequestedExpiry()
    {
        string userId = await McpTestHelper.CreateUserAsync(factory, "mcp-custom-expiry");

        using var scope = factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<McpApiTokenService>();
        DateTime requestedExpiry = DateTime.UtcNow.AddMonths(3);

        (_, var entity) = await tokenService.CreateTokenAsync(userId, "custom-expiry", requestedExpiry);

        await Assert.That(entity.ExpiresAt).IsNotNull();
        await Assert.That(entity.ExpiresAt!.Value).IsEqualTo(requestedExpiry);
    }

    [Test]
    public async Task CreateTokenAsync_WithExpiryBeyondSixMonths_Throws()
    {
        string userId = await McpTestHelper.CreateUserAsync(factory, "mcp-max-expiry");

        using var scope = factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<McpApiTokenService>();
        DateTime requestedExpiry = McpApiTokenService.GetDefaultExpirationUtc(DateTime.UtcNow).AddDays(2);

        await Assert.That(() => tokenService.CreateTokenAsync(userId, "too-long", requestedExpiry))
            .Throws<ArgumentOutOfRangeException>()
            .WithMessageContaining(McpApiTokenService.MaxExpiryValidationMessage);
    }

    [Test]
    public async Task CreateTokenAsync_WithExplicitCreatedAt_UsesReferenceTimeForDefaultExpiry()
    {
        string userId = await McpTestHelper.CreateUserAsync(factory, "mcp-explicit-created-at");

        using var scope = factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<McpApiTokenService>();
        DateTime createdAtUtc = new(2026, 4, 30, 23, 59, 59, DateTimeKind.Utc);

        (_, var entity) = await tokenService.CreateTokenAsync(
            userId,
            "explicit-created-at",
            createdAtUtc: createdAtUtc);

        await Assert.That(entity.CreatedAt).IsEqualTo(createdAtUtc);
        await Assert.That(entity.ExpiresAt).IsNotNull();
        await Assert.That(entity.ExpiresAt!.Value)
            .IsEqualTo(McpApiTokenService.GetDefaultExpirationUtc(createdAtUtc));
    }
}
