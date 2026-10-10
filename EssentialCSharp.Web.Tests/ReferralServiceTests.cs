using System.Security.Claims;
using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Data;
using EssentialCSharp.Web.Extensions;
using EssentialCSharp.Web.Services.Referrals;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EssentialCSharp.Web.Tests;

public class ReferralServiceTests : IntegrationTestBase
{
    [Test]
    public async Task EnsureReferralIdAsync_NullUser_ReturnsNull()
    {
        string? referralId = await InServiceScopeAsync(async services =>
            await services.GetRequiredService<IReferralService>().EnsureReferralIdAsync(null));

        await Assert.That(referralId).IsNull();
    }

    [Test]
    public async Task TrackReferralAsync_ExistingReferrer_IncrementsReferralCount()
    {
        string userId = CreateUserId();
        string referralId = await CreateUserWithReferralIdAsync(userId);
        await CreateUserAsync(CreateUserId());

        await InServiceScopeAsync(async services =>
        {
            services.GetRequiredService<IReferralService>().TrackReferralAsync(referralId, null);
            await services.GetRequiredService<EssentialCSharpWebContext>().SaveChangesAsync();
        });

        int referralCount = await GetReferralCountAsync(userId);
        await Assert.That(referralCount).IsEqualTo(1);
    }

    [Test]
    public async Task TrackReferralAsync_UserHasSameReferralId_DoesNotIncrementReferralCount()
    {
        string userId = CreateUserId();
        string referralId = await CreateUserWithReferralIdAsync(userId);
        ClaimsPrincipal user = new(new ClaimsIdentity(
        [
            new Claim(ClaimsExtensions.ReferrerIdClaimType, referralId)
        ]));

        await InServiceScopeAsync(async services =>
        {
            services.GetRequiredService<IReferralService>().TrackReferralAsync(referralId, user);
            await services.GetRequiredService<EssentialCSharpWebContext>().SaveChangesAsync();
        });

        int referralCount = await GetReferralCountAsync(userId);
        await Assert.That(referralCount).IsEqualTo(0);
    }

    [Test]
    public async Task TrackReferralAsync_UnknownReferralId_DoesNotThrowOrChangeUsers()
    {
        string userId = CreateUserId();
        await CreateUserAsync(userId);

        await InServiceScopeAsync(async services =>
        {
            services.GetRequiredService<IReferralService>().TrackReferralAsync("unknown", null);
            await services.GetRequiredService<EssentialCSharpWebContext>().SaveChangesAsync();
        });

        int referralCount = await GetReferralCountAsync(userId);
        await Assert.That(referralCount).IsEqualTo(0);
    }

    private async Task<string> CreateUserWithReferralIdAsync(string userId)
    {
        await CreateUserAsync(userId);

        return await InServiceScopeAsync(async services =>
        {
            EssentialCSharpWebContext dbContext =
                services.GetRequiredService<EssentialCSharpWebContext>();
            UserManager<EssentialCSharpWebUser> userManager =
                services.GetRequiredService<UserManager<EssentialCSharpWebUser>>();
            EssentialCSharpWebUser user = await userManager.FindByIdAsync(userId)
                ?? throw new InvalidOperationException($"Test user '{userId}' was not created.");

            string referralId = await services.GetRequiredService<IReferralService>()
                .EnsureReferralIdAsync(user)
                ?? throw new InvalidOperationException("A referral ID was not generated.");
            await dbContext.SaveChangesAsync();
            return referralId;
        });
    }

    private async Task CreateUserAsync(string userId)
    {
        _ = Factory;
        await InServiceScopeAsync(async services =>
        {
            UserManager<EssentialCSharpWebUser> userManager =
                services.GetRequiredService<UserManager<EssentialCSharpWebUser>>();
            IdentityResult result = await userManager.CreateAsync(
                new EssentialCSharpWebUser
                {
                    Id = userId,
                    UserName = $"{userId}@example.test",
                    Email = $"{userId}@example.test"
                });
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not create test user: {string.Join(", ", result.Errors.Select(error => error.Description))}");
            }
        });
    }

    private async Task<int> GetReferralCountAsync(string userId)
    {
        _ = Factory;
        return await InServiceScopeAsync(async services =>
            await services.GetRequiredService<EssentialCSharpWebContext>().Users
                .Where(user => user.Id == userId)
                .Select(user => user.ReferralCount)
                .SingleAsync());
    }

    private static string CreateUserId() => $"user-{Guid.NewGuid():N}";
}
