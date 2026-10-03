using EssentialCSharp.Chat.Common.Services;

namespace EssentialCSharp.Chat.Tests;

public class ChatContentFilterErrorClassifierTests
{
    [Test]
    [Arguments("content_filter")]
    [Arguments("ContentFilter")]
    [Arguments("ResponsibleAIPolicyViolation")]
    [Arguments("content_policy_violation")]
    public async Task IsContentFilterFailure_RecognizesProviderCodes(string code)
    {
        await Assert.That(ChatContentFilterErrorClassifier.IsContentFilterFailure(code, null, null)).IsTrue();
    }

    [Test]
    public async Task IsContentFilterFailure_RecognizesStreamingIncompleteReason()
    {
        await Assert.That(ChatContentFilterErrorClassifier.IsContentFilterFailure(null, "content_filter", null)).IsTrue();
    }

    [Test]
    public async Task ContainsContentFilterErrorCode_RecognizesNestedProviderCode()
    {
        const string responseBody = """
            {
              "error": {
                "code": "invalid_request_error",
                "innererror": {
                  "code": "ResponsibleAIPolicyViolation"
                }
              }
            }
            """;

        await Assert.That(ChatContentFilterErrorClassifier.ContainsContentFilterErrorCode(responseBody)).IsTrue();
    }

    [Test]
    public async Task ContainsContentFilterErrorCode_ReturnsFalseForUnrelatedOrMalformedErrors()
    {
        await Assert.That(ChatContentFilterErrorClassifier.ContainsContentFilterErrorCode(
            """{"error":{"code":"context_length_exceeded"}}""")).IsFalse();
        await Assert.That(ChatContentFilterErrorClassifier.ContainsContentFilterErrorCode("{invalid json")).IsFalse();
    }
}
