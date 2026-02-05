namespace EssentialCSharp.Web.Models;

public class ListingSourceCodeResponse
{
    public int ChapterNumber { get; set; }
    public int ListingNumber { get; set; }
    public string FileExtension { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
