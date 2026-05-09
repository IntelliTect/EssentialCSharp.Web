using EssentialCSharp.Web.Models;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EssentialCSharp.Web.Tests;

[NotInParallel("McpTests")]
[ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerClass)]
public class McpApiTokenServiceTests(WebApplicationFactory factory)
{
    private readonly List<IServiceScope> _scopes = [];

    [After(Test)]
    public void DisposeScopes()
    {
        foreach (var scope in _scopes)
            scope.Dispose();
        _scopes.Clear();
    }

    private async Task<(string UserId, McpApiTokenService TokenService)> ArrangeAsync(string prefix)
    {
        string userId = await McpTestHelper.CreateUserAsync(factory, prefix);
        var scope = factory.Services.CreateScope();
        _scopes.Add(scope);
        var tokenService = scope.ServiceProvider.GetRequiredService<McpApiTokenService>();
        return (userId, tokenService);
    }

    private async Task<McpApiTokenService> FillToLimitAsync(string userId)
    {
        var scope = factory.Services.CreateScope();
        _scopes.Add(scope);
        var tokenService = scope.ServiceProvider.GetRequiredService<McpApiTokenService>();
        for (int i = 0; i < McpApiTokenService.MaxTokensPerUser; i++)
        {
            await tokenService.CreateTokenAsync(userId, $"token-{i}");
        }
        return tokenService;
    }

    [Test]
    public async Task CreateTokenAsync_WithoutExpiry_UsesSixMonthDefault()
    {
        var (userId, tokenService) = await ArrangeAsync("mcp-default-expiry");

        (_, var entity) = await tokenService.CreateTokenAsync(userId, "default-expiry");

        await Assert.That(entity.ExpiresAt).IsNotNull();
        await Assert.That(entity.ExpiresAt!.Value)
            .IsEqualTo(McpApiTokenService.GetDefaultExpirationUtc(entity.CreatedAt));
    }

    [Test]
    public async Task CreateTokenAsync_WithExpiryWithinSixMonths_UsesRequestedExpiry()
    {
        var (userId, tokenService) = await ArrangeAsync("mcp-custom-expiry");
        DateTime requestedExpiry = DateTime.UtcNow.AddMonths(3);

        (_, var entity) = await tokenService.CreateTokenAsync(userId, "custom-expiry", requestedExpiry);

        await Assert.That(entity.ExpiresAt).IsNotNull();
        await Assert.That(entity.ExpiresAt!.Value).IsEqualTo(requestedExpiry);
    }

    [Test]
    public async Task CreateTokenAsync_WithExpiryBeyondSixMonths_Throws()
    {
        var (userId, tokenService) = await ArrangeAsync("mcp-max-expiry");
        DateTime requestedExpiry = McpApiTokenService.GetDefaultExpirationUtc(DateTime.UtcNow).AddDays(2);

        await Assert.That(() => tokenService.CreateTokenAsync(userId, "too-long", requestedExpiry))
            .Throws<ArgumentOutOfRangeException>()
            .WithMessageContaining(McpApiTokenService.MaxExpiryValidationMessage);
    }

    [Test]
    public async Task CreateTokenAsync_WithExplicitCreatedAt_UsesReferenceTimeForDefaultExpiry()
    {
        var (userId, tokenService) = await ArrangeAsync("mcp-explicit-created-at");
        DateTime createdAtUtc = new(2026, 4, 30, 23, 59, 59, DateTimeKind.Utc);

        (_, var entity) = await tokenService.CreateTokenAsync(
            userId, "explicit-created-at", createdAtUtc: createdAtUtc);

        await Assert.That(entity.CreatedAt).IsEqualTo(createdAtUtc);
        await Assert.That(entity.ExpiresAt).IsNotNull();
        await Assert.That(entity.ExpiresAt!.Value)
            .IsEqualTo(McpApiTokenService.GetDefaultExpirationUtc(createdAtUtc));
    }

    [Test]
    public async Task GetActiveTokenCountAsync_NoTokens_ReturnsZero()
    {
        var (userId, tokenService) = await ArrangeAsync("mcp-count-zero");

        int count = await tokenService.GetActiveTokenCountAsync(userId);

        await Assert.That(count).IsEqualTo(0);
    }

    [Test]
    public async Task GetActiveTokenCountAsync_ActiveTokens_CountsAll()
    {
        var (userId, tokenService) = await ArrangeAsync("mcp-count-active");

        await tokenService.CreateTokenAsync(userId, "token-1");
        await tokenService.CreateTokenAsync(userId, "token-2");
        await tokenService.CreateTokenAsync(userId, "token-3");

        int count = await tokenService.GetActiveTokenCountAsync(userId);

        await Assert.That(count).IsEqualTo(3);
    }

    [Test]
    public async Task GetActiveTokenCountAsync_RevokedToken_ExcludedFromCount()
    {
        var (userId, tokenService) = await ArrangeAsync("mcp-count-revoked");

        await tokenService.CreateTokenAsync(userId, "active-token");
        (_, var revokedEntity) = await tokenService.CreateTokenAsync(userId, "revoked-token");
        await tokenService.RevokeTokenAsync(revokedEntity.Id, userId);

        int count = await tokenService.GetActiveTokenCountAsync(userId);

        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task GetActiveTokenCountAsync_ExpiredToken_ExcludedFromCount()
    {
        var (userId, tokenService) = await ArrangeAsync("mcp-count-expired");

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
        var (userId, _) = await ArrangeAsync("mcp-at-limit");
        var tokenService = await FillToLimitAsync(userId);

        await Assert.That(() => tokenService.CreateTokenAsync(userId, "one-too-many"))
            .Throws<TokenLimitExceededException>();
    }

    [Test]
    public async Task CreateTokenAsync_AfterRevokingAtLimit_AllowsNewToken()
    {
        var (userId, _) = await ArrangeAsync("mcp-revoke-then-create");
        var tokenService = await FillToLimitAsync(userId);

        // Revoke the last token to free a slot
        var tokens = await tokenService.GetUserTokensAsync(userId);
        await tokenService.RevokeTokenAsync(tokens[0].Id, userId);

        // Should now succeed — active count dropped below max
        (_, var newEntity) = await tokenService.CreateTokenAsync(userId, "replacement");
        await Assert.That(newEntity).IsNotNull();
        int activeCount = await tokenService.GetActiveTokenCountAsync(userId);
        await Assert.That(activeCount).IsEqualTo(McpApiTokenService.MaxTokensPerUser);
    }
}
