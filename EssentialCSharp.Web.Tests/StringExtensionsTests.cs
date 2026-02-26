namespace EssentialCSharp.Web.Extensions.Tests;

public class StringExtensionsTests
{
    [Test]
    [Arguments(" ExtraSpacing ", "extraspacing")]
    [Arguments("Hello World", "hello-world")]
    [Arguments("Coding the Publish–Subscribe Pattern with Multicast Delegates", "coding-the-publish-subscribe-pattern-with-multicast-delegates")]
    [Arguments("C#", "c")]
    [Arguments("C# Syntax Fundamentals", "c-syntax-fundamentals")]
    [Arguments("C#_Syntax_Fundamentals", "c-syntax-fundamentals")]
    [Arguments("C# Syntax_Fundamentals-for-me", "c-syntax-fundamentals-for-me")]
    [Arguments("Bitwise Operators (<<, >>, |, &, ^, ~)", "bitwise-operators")]
    [Arguments(".NET Standard", "net-standard")]
    [Arguments("Working with System.Threading", "working-with-system-threading")]
    public async Task SanitizeStringToOnlyHaveDashesAndLowerCase(string actual, string sanitized)
    {
        await Assert.That(actual.Sanitize()).IsEqualTo(sanitized);
        await Assert.That(actual.Sanitize().Sanitize()).IsEqualTo(sanitized);
    }

    [Test]
    [Arguments("hello-world#hello-world", "hello-world")]
    [Arguments("C#Syntax#hello-world", "csyntax")]
    [Arguments("C#Syntax", "csyntax")]
    [Arguments("cSyntax", "csyntax")]
    [Arguments(".NET", "net")]
    [Arguments("System.Threading", "system-threading")]
    public async Task GetPotentialMatches(string actual, string match)
    {
        var matches = actual.GetPotentialMatches().ToList();
        await Assert.That(matches).Contains(match);
    }
}