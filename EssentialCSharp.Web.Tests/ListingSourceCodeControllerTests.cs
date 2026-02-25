using System.Net;
using System.Net.Http.Json;
using EssentialCSharp.Web.Models;

namespace EssentialCSharp.Web.Tests;

[ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerClass)]
public class ListingSourceCodeControllerTests(WebApplicationFactory factory)
{
    [Test]
    public async Task GetListing_WithValidChapterAndListing_Returns200WithContent()
    {
        // Arrange
        HttpClient client = factory.CreateClient();

        // Act
        using HttpResponseMessage response = await client.GetAsync("/api/ListingSourceCode/chapter/1/listing/1");
        
        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        ListingSourceCodeResponse? result = await response.Content.ReadFromJsonAsync<ListingSourceCodeResponse>();
        await Assert.That(result).IsNotNull();
        using (Assert.Multiple())
        {
            await Assert.That(result.ChapterNumber).IsEqualTo(1);
            await Assert.That(result.ListingNumber).IsEqualTo(1);
            await Assert.That(result.FileExtension).IsNotEmpty();
            await Assert.That(result.Content).IsNotEmpty();
        }
    }


    [Test]
    public async Task GetListing_WithInvalidChapter_Returns404()
    {
        // Arrange
        HttpClient client = factory.CreateClient();

        // Act
        using HttpResponseMessage response = await client.GetAsync("/api/ListingSourceCode/chapter/999/listing/1");
        
        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetListing_WithInvalidListing_Returns404()
    {
        // Arrange
        HttpClient client = factory.CreateClient();

        // Act
        using HttpResponseMessage response = await client.GetAsync("/api/ListingSourceCode/chapter/1/listing/999");
        
        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetListingsByChapter_WithValidChapter_ReturnsMultipleListings()
    {
        // Arrange
        HttpClient client = factory.CreateClient();

        // Act
        using HttpResponseMessage response = await client.GetAsync("/api/ListingSourceCode/chapter/1");
        
        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        List<ListingSourceCodeResponse>? results = await response.Content.ReadFromJsonAsync<List<ListingSourceCodeResponse>>();
        await Assert.That(results).IsNotNull();
        await Assert.That(results).IsNotEmpty();

        // Verify all results are from chapter 1
        foreach (var r in results)
        {
            using (Assert.Multiple())
            {
                await Assert.That(r.ChapterNumber).IsEqualTo(1);
            }
        }

        // Verify results are ordered by listing number
        await Assert.That(results).IsOrderedBy(r => r.ListingNumber);

        // Verify each listing has required properties
        foreach (var r in results)
        {
            using (Assert.Multiple())
            {
                await Assert.That(r.FileExtension).IsNotEmpty();
                await Assert.That(r.Content).IsNotEmpty();
            }
        }
    }

    [Test]
    public async Task GetListingsByChapter_WithInvalidChapter_ReturnsEmptyList()
    {
        // Arrange
        HttpClient client = factory.CreateClient();

        // Act
        using HttpResponseMessage response = await client.GetAsync("/api/ListingSourceCode/chapter/999");
        
        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        List<ListingSourceCodeResponse>? results = await response.Content.ReadFromJsonAsync<List<ListingSourceCodeResponse>>();
        await Assert.That(results).IsNotNull();
        await Assert.That(results).IsEmpty();
    }
}