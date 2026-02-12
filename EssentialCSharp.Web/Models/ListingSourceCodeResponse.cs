namespace EssentialCSharp.Web.Models;

public record class ListingSourceCodeResponse(
    int ChapterNumber,
    int ListingNumber,
    string FileExtension = string.Empty,
    string Content = string.Empty);