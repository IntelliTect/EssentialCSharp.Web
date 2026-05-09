namespace EssentialCSharp.Web.Extensions;

public static class GuidelineTypeExtensions
{
    public static string ToDisplayString(this GuidelineType type) => type switch
    {
        GuidelineType.Do => "DO",
        GuidelineType.Consider => "CONSIDER",
        GuidelineType.Avoid => "AVOID",
        GuidelineType.DoNot => "DO NOT",
        _ => "NOTE"
    };
}
