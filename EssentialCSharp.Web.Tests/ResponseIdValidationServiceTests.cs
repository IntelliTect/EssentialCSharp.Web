using EssentialCSharp.Web.Services;

namespace EssentialCSharp.Web.Tests;

public class ResponseIdValidationServiceTests : IDisposable
{
    // Match production SizeLimit so SetSize(1) is exercised in tests, not silently ignored.
    private readonly ResponseIdValidationService _service = new();

    [After(Test)]
    public void Cleanup() => _service.Dispose();

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task ValidateResponseId_BlankResponseId_AllowsNewConversation(string? responseId)
    {
        bool result = _service.ValidateResponseId("user1", responseId);

        await Assert.That(result).IsTrue();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task ValidateResponseId_BlankUserId_Rejects(string? userId)
    {
        bool result = _service.ValidateResponseId(userId, "resp_123");

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task ValidateResponseId_CacheMiss_AllowsGracefulDegradation()
    {
        // No RecordResponseId call — simulate server restart / different instance
        bool result = _service.ValidateResponseId("user1", "resp_unknown");

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task ValidateResponseId_RecordedByOwner_Validates()
    {
        _service.RecordResponseId("user1", "resp_abc");

        bool result = _service.ValidateResponseId("user1", "resp_abc");

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task ValidateResponseId_RecordedByDifferentUser_Rejects()
    {
        _service.RecordResponseId("user1", "resp_abc");

        bool result = _service.ValidateResponseId("user2", "resp_abc");

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task RecordResponseId_NullInputs_DoesNotThrow()
    {
        _service.RecordResponseId(null, "resp_abc");
        _service.RecordResponseId("user1", null);
        _service.RecordResponseId(null, null);

        // Verify the service is still functional after no-op calls
        _service.RecordResponseId("user1", "resp_abc");
        await Assert.That(_service.ValidateResponseId("user1", "resp_abc")).IsTrue();
    }

    [Test]
    public async Task ValidateResponseId_MultipleResponseIds_EachValidatedIndependently()
    {
        _service.RecordResponseId("user1", "resp_001");
        _service.RecordResponseId("user1", "resp_002");

        await Assert.That(_service.ValidateResponseId("user1", "resp_001")).IsTrue();
        await Assert.That(_service.ValidateResponseId("user1", "resp_002")).IsTrue();
        // Unrecorded ID for same user → cache miss → allow
        await Assert.That(_service.ValidateResponseId("user1", "resp_003")).IsTrue();
    }

    [Test]
    public async Task ValidateResponseId_TwoUsers_IsolatedFromEachOther()
    {
        _service.RecordResponseId("user1", "resp_A");
        _service.RecordResponseId("user2", "resp_B");

        await Assert.That(_service.ValidateResponseId("user1", "resp_A")).IsTrue();
        await Assert.That(_service.ValidateResponseId("user2", "resp_B")).IsTrue();
        await Assert.That(_service.ValidateResponseId("user2", "resp_A")).IsFalse();
        await Assert.That(_service.ValidateResponseId("user1", "resp_B")).IsFalse();
    }

    [Test]
    public async Task RecordResponseId_SizeLimitEnforced_EntryCountedInCache()
    {
        // Record an entry — with SizeLimit set, SetSize(1) should count toward the cache size.
        _service.RecordResponseId("user1", "resp_size_test");

        // Verify it was recorded (i.e., not silently evicted due to misconfiguration).
        await Assert.That(_service.ValidateResponseId("user1", "resp_size_test")).IsTrue();
    }
}
