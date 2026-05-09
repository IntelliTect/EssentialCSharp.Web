namespace EssentialCSharp.Web.Services;

public sealed class SiteSettings
{
    public const string SectionName = "SiteSettings";

    public string BaseUrl { get; set; } = "https://essentialcsharp.com";
}
