using EssentialCSharp.Web.Services;
using EssentialCSharp.Web.Tools;
using Moq;

namespace EssentialCSharp.Web.Tests;

public class BookGuidelinesToolTests
{
    private static BookGuidelinesTool CreateTool()
    {
        Mock<IGuidelinesService> guidelines = new();
        guidelines.SetupGet(g => g.Guidelines).Returns([]);
        return new BookGuidelinesTool(guidelines.Object);
    }

    [Test]
    public async Task GetCSharpGuidelines_TopicOverTheLimit_IsRejectedWithoutBeingEchoed()
    {
        string topic = string.Join(' ', Enumerable.Repeat("word", 200));

        string result = CreateTool().GetCSharpGuidelines(topic: topic);

        using (Assert.Multiple())
        {
            await Assert.That(result).IsEqualTo("Topic is too long (maximum 500 characters).");
            await Assert.That(result).DoesNotContain("word word");
        }
    }

    [Test]
    public async Task GetCSharpGuidelines_TopicAtTheLimit_IsSearched()
    {
        string topic = new('a', BookGuidelinesTool.MaxTopicLength);

        string result = CreateTool().GetCSharpGuidelines(topic: topic);

        await Assert.That(result).StartsWith("No guidelines found related to", StringComparison.Ordinal);
    }
}
