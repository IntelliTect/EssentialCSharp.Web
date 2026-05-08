using EssentialCSharp.Web.Models;
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

    [Test]
    public async Task GetActiveTokenCountAsync_NoTokens_ReturnsZero()
    {
        string userId = await McpTestHelper.CreateUserAsync(factory, "mcp-count-zero");

        using var scope = factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<McpApiTokenService>();

        int count = await tokenService.GetActiveTokenCountAsync(userId);

        await Assert.That(count).IsEqualTo(0);
    }

    [Test]
    public async Task GetActiveTokenCountAsync_ActiveTokens_CountsAll()
    {
        string userId = await McpTestHelper.CreateUserAsync(factory, "mcp-count-active");

        using var scope = factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<McpApiTokenService>();

        await tokenService.CreateTokenAsync(userId, "token-1");
        await tokenService.CreateTokenAsync(userId, "token-2");
        await tokenService.CreateTokenAsync(userId, "token-3");

        int count = await tokenService.GetActiveTokenCountAsync(userId);

        await Assert.That(count).IsEqualTo(3);
    }

    [Test]
    public async Task GetActiveTokenCountAsync_RevokedToken_ExcludedFromCount()
    {
        string userId = await McpTestHelper.CreateUserAsync(factory, "mcp-count-revoked");

        using var scope = factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<McpApiTokenService>();

        await tokenService.CreateTokenAsync(userId, "active-token");
        (_, var revokedEntity) = await tokenService.CreateTokenAsync(userId, "revoked-token");
        await tokenService.RevokeTokenAsync(revokedEntity.Id, userId);

        int count = await tokenService.GetActiveTokenCountAsync(userId);

        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task GetActiveTokenCountAsync_ExpiredToken_ExcludedFromCount()
    {
        string userId = await McpTestHelper.CreateUserAsync(factory, "mcp-count-expired");

        using var scope = factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<McpApiTokenService>();

        // Create a token that has already expired:
        // createdAt 7 months ago → max expiry = 1 month ago; use 2 months ago as expiresAt
        DateTime createdAt = DateTime.UtcNow.AddMonths(-7);
        DateTime pastExpiry = DateTime.UtcNow.AddMonths(-2);
        await tokenService.CreateTokenAsync(userId, "expired-token",
            expiresAt: pastExpiry, createdAtUtc: createdAt);
        await tokenService.CreateTokenAsync(userId, "valid-token");

        int count = await tokenService.GetActiveTokenCountAsync(userId);

        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task CreateTokenAsync_AtMaxLimit_ThrowsTokenLimitExceededException()
    {
        string userId = await McpTestHelper.CreateUserAsync(factory, "mcp-at-limit");

        using var scope = factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<McpApiTokenService>();

        for (int i = 0; i < McpApiTokenService.MaxTokensPerUser; i++)
        {
            await tokenService.CreateTokenAsync(userId, $"token-{i}");
        }

        await Assert.That(() => tokenService.CreateTokenAsync(userId, "one-too-many"))
            .Throws<TokenLimitExceededException>();
    }

    [Test]
    public async Task CreateTokenAsync_AfterRevokingAtLimit_AllowsNewToken()
    {
        string userId = await McpTestHelper.CreateUserAsync(factory, "mcp-revoke-then-create");

        using var scope = factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<McpApiTokenService>();

        // Fill up to limit
        McpApiToken? lastEntity = null;
        for (int i = 0; i < McpApiTokenService.MaxTokensPerUser; i++)
        {
            (_, lastEntity) = await tokenService.CreateTokenAsync(userId, $"token-{i}");
        }

        // Revoke one
        await tokenService.RevokeTokenAsync(lastEntity!.Id, userId);

        // Should now succeed — active count dropped below max
        (_, var newEntity) = await tokenService.CreateTokenAsync(userId, "replacement");
        await Assert.That(newEntity).IsNotNull();
        int activeCount = await tokenService.GetActiveTokenCountAsync(userId);
        await Assert.That(activeCount).IsEqualTo(McpApiTokenService.MaxTokensPerUser);
    }
}
