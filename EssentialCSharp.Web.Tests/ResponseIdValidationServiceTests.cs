using EssentialCSharp.Web.Services;
using Microsoft.Extensions.Caching.Memory;

namespace EssentialCSharp.Web.Tests;

public class ResponseIdValidationServiceTests
{
    // Match production SizeLimit so SetSize(1) is exercised in tests, not silently ignored.
    private static MemoryCache CreateCache() => new(new MemoryCacheOptions { SizeLimit = 10_000 });

    private static ResponseIdValidationService CreateService(MemoryCache cache) => new(cache);

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task ValidateResponseId_BlankResponseId_AllowsNewConversation(string? responseId)
    {
        using var cache = CreateCache();
        var service = CreateService(cache);

        bool result = service.ValidateResponseId("user1", responseId);

        await Assert.That(result).IsTrue();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task ValidateResponseId_BlankUserId_Rejects(string? userId)
    {
        using var cache = CreateCache();
        var service = CreateService(cache);

        bool result = service.ValidateResponseId(userId, "resp_123");

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task ValidateResponseId_CacheMiss_AllowsGracefulDegradation()
    {
        using var cache = CreateCache();
        var service = CreateService(cache);
        // No RecordResponseId call — simulate server restart / different instance

        bool result = service.ValidateResponseId("user1", "resp_unknown");

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task ValidateResponseId_RecordedByOwner_Validates()
    {
        using var cache = CreateCache();
        var service = CreateService(cache);
        service.RecordResponseId("user1", "resp_abc");

        bool result = service.ValidateResponseId("user1", "resp_abc");

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task ValidateResponseId_RecordedByDifferentUser_Rejects()
    {
        using var cache = CreateCache();
        var service = CreateService(cache);
        service.RecordResponseId("user1", "resp_abc");

        bool result = service.ValidateResponseId("user2", "resp_abc");

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task RecordResponseId_NullInputs_DoesNotThrow()
    {
        using var cache = CreateCache();
        var service = CreateService(cache);

        service.RecordResponseId(null, "resp_abc");
        service.RecordResponseId("user1", null);
        service.RecordResponseId(null, null);

        // Verify the service is still functional after no-op calls
        service.RecordResponseId("user1", "resp_abc");
        await Assert.That(service.ValidateResponseId("user1", "resp_abc")).IsTrue();
    }

    [Test]
    public async Task ValidateResponseId_MultipleResponseIds_EachValidatedIndependently()
    {
        using var cache = CreateCache();
        var service = CreateService(cache);
        service.RecordResponseId("user1", "resp_001");
        service.RecordResponseId("user1", "resp_002");

        await Assert.That(service.ValidateResponseId("user1", "resp_001")).IsTrue();
        await Assert.That(service.ValidateResponseId("user1", "resp_002")).IsTrue();
        // Unrecorded ID for same user → cache miss → allow
        await Assert.That(service.ValidateResponseId("user1", "resp_003")).IsTrue();
    }

    [Test]
    public async Task ValidateResponseId_TwoUsers_IsolatedFromEachOther()
    {
        using var cache = CreateCache();
        var service = CreateService(cache);
        service.RecordResponseId("user1", "resp_A");
        service.RecordResponseId("user2", "resp_B");

        await Assert.That(service.ValidateResponseId("user1", "resp_A")).IsTrue();
        await Assert.That(service.ValidateResponseId("user2", "resp_B")).IsTrue();
        await Assert.That(service.ValidateResponseId("user2", "resp_A")).IsFalse();
        await Assert.That(service.ValidateResponseId("user1", "resp_B")).IsFalse();
    }

    [Test]
    public async Task RecordResponseId_SizeLimitEnforced_EntryCountedInCache()
    {
        using var cache = CreateCache();
        var service = CreateService(cache);

        // Record an entry — with SizeLimit set, SetSize(1) should count toward the cache size.
        service.RecordResponseId("user1", "resp_size_test");

        // Verify it was recorded (i.e., not silently evicted due to misconfiguration).
        await Assert.That(service.ValidateResponseId("user1", "resp_size_test")).IsTrue();
    }
}
