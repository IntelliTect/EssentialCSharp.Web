using EssentialCSharp.Web.Models;

namespace EssentialCSharp.Web.Services;

public interface ITypesenseSearchService
{
    Task<SearchResult> SearchAsync(string query, int page = 1, int perPage = 10, CancellationToken cancellationToken = default);
    Task<bool> IndexDocumentAsync(SearchDocument document, CancellationToken cancellationToken = default);
    Task<bool> IndexDocumentsAsync(IEnumerable<SearchDocument> documents, CancellationToken cancellationToken = default);
    Task<bool> DeleteDocumentAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> InitializeCollectionAsync(CancellationToken cancellationToken = default);
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}