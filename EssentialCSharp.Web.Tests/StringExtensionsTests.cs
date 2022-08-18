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
    public void SanitizeStringToOnlyHaveDashesAndLowerCase(string actual, string sanitized)
    {
        Assert.Equal(actual.SanitizeKey(), sanitized);
    }
}
