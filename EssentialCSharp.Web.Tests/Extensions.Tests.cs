namespace EssentialCSharp.Web.Extensions.Tests;

public class StringExtenstionsTests
{
    [Theory]
    [InlineData(" ExtraSpacing ", "extraspacing")]
    [InlineData("Hello World", "hello-world")]
    [InlineData("Coding the Publish–Subscribe Pattern with Multicast Delegates", "coding-the-publish-subscribe-pattern-with-multicast-delegates")]
    [InlineData("C#", "c")]
    [InlineData("C# Syntax Fundamentals", "c-syntax-fundamentals")]
    [InlineData("C#_Syntax_Fundamentals", "c-syntax-fundamentals")]
    [InlineData("C# Syntax_Fundamentals-for-me", "c-syntax-fundamentals-for-me")]
    [InlineData("Bitwise Operators (<<, >>, |, &, ^, ~)", "bitwise-operators")]
    [InlineData(".NET Standard", "net-standard")]
    [InlineData("Working with System.Threading", "working-with-system-threading")]
    public void SanitizeStringToOnlyHaveDashesAndLowerCase(string actual, string sanitized)
    {
        Assert.Equal(sanitized, actual.Sanitize());
        Assert.Equal(sanitized, actual.Sanitize().Sanitize());
    }

    [Theory]
    [InlineData("hello-world#hello-world", "hello-world")]
    [InlineData("C#Syntax#hello-world", "csyntax")]
    [InlineData("C#Syntax", "csyntax")]
    [InlineData("cSyntax", "csyntax")]
    [InlineData(".NET", "net")]
    [InlineData("System.Threading", "system-threading")]
    public void GetPotentialMatches(string actual, string match)
    {
        var matches = actual.GetPotentialMatches().ToList();
        Assert.Contains(match, matches);
    }
}
