using EssentialCSharp.Web.Services;
using Microsoft.Extensions.Caching.Memory;

namespace EssentialCSharp.Web.Tests;

public class ResponseIdValidationServiceTests
{
    private static ResponseIdValidationService CreateService()
        => new(new MemoryCache(new MemoryCacheOptions()));

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task ValidateResponseId_BlankResponseId_AllowsNewConversation(string? responseId)
    {
        var service = CreateService();

        bool result = service.ValidateResponseId("user1", responseId);

        await Assert.That(result).IsTrue();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task ValidateResponseId_BlankUserId_Rejects(string? userId)
    {
        var service = CreateService();

        bool result = service.ValidateResponseId(userId, "resp_123");

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task ValidateResponseId_CacheMiss_AllowsGracefulDegradation()
    {
        var service = CreateService();
        // No RecordResponseId call — simulate server restart / different instance

        bool result = service.ValidateResponseId("user1", "resp_unknown");

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task ValidateResponseId_RecordedByOwner_Validates()
    {
        var service = CreateService();
        service.RecordResponseId("user1", "resp_abc");

        bool result = service.ValidateResponseId("user1", "resp_abc");

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task ValidateResponseId_RecordedByDifferentUser_Rejects()
    {
        var service = CreateService();
        service.RecordResponseId("user1", "resp_abc");

        bool result = service.ValidateResponseId("user2", "resp_abc");

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task RecordResponseId_NullInputs_DoesNotThrow()
    {
        var service = CreateService();

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
        var service = CreateService();
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
        var service = CreateService();
        service.RecordResponseId("user1", "resp_A");
        service.RecordResponseId("user2", "resp_B");

        await Assert.That(service.ValidateResponseId("user1", "resp_A")).IsTrue();
        await Assert.That(service.ValidateResponseId("user2", "resp_B")).IsTrue();
        await Assert.That(service.ValidateResponseId("user2", "resp_A")).IsFalse();
        await Assert.That(service.ValidateResponseId("user1", "resp_B")).IsFalse();
    }
}
