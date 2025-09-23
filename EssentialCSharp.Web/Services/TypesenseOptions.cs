namespace EssentialCSharp.Web.Services;

public class TypesenseOptions
{
    public const string SectionName = "TypesenseOptions";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 8108;
    public string Protocol { get; set; } = "http";
    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
}