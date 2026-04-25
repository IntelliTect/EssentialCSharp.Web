namespace EssentialCSharp.Web.Tools;

internal static class BookToolHelpers
{
    internal static string NormalizeExtension(string ext) =>
        ext.TrimStart('.').ToLowerInvariant() switch
        {
            "cs" => "csharp",
            "vb" => "vbnet",
            "fs" => "fsharp",
            "" => "",
            var e => e
        };
}
