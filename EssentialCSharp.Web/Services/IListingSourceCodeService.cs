using EssentialCSharp.Web.Models;

namespace EssentialCSharp.Web.Services;

public interface IListingSourceCodeService
{
    Task<ListingSourceCodeResponse?> GetListingAsync(int chapterNumber, int listingNumber);
    Task<IReadOnlyList<ListingSourceCodeResponse>> GetListingsByChapterAsync(int chapterNumber);
}
