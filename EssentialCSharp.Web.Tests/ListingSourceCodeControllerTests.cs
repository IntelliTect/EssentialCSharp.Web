using System.Net;
using System.Net.Http.Json;
using EssentialCSharp.Web.Models;
using System.Threading.Tasks;

namespace EssentialCSharp.Web.Tests;

public class ListingSourceCodeControllerTests
{
    [Test]
    public async Task GetListing_WithValidChapterAndListing_Returns200WithContent()
    {
        // Arrange
        using WebApplicationFactory factory = new();
        HttpClient client = factory.CreateClient();

        // Act
        using HttpResponseMessage response = await client.GetAsync("/api/ListingSourceCode/chapter/1/listing/1");
        
        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        ListingSourceCodeResponse? result = await response.Content.ReadFromJsonAsync<ListingSourceCodeResponse>();
        await Assert.That(result).IsNotNull();
        await Assert.That(result.ChapterNumber).IsEqualTo(1);
        await Assert.That(result.ListingNumber).IsEqualTo(1);
        await Assert.That(result.FileExtension).IsNotEmpty();
        await Assert.That(result.Content).IsNotEmpty();
    }


    [Test]
    public async Task GetListing_WithInvalidChapter_Returns404()
    {
        // Arrange
        using WebApplicationFactory factory = new();
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
        using WebApplicationFactory factory = new();
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
        using WebApplicationFactory factory = new();
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
            await Assert.That(r.ChapterNumber).IsEqualTo(1);
        }

        // Verify results are ordered by listing number
        await Assert.That(results).IsEquivalentTo(results.OrderBy(r => r.ListingNumber).ToList());

        // Verify each listing has required properties
        foreach (var r in results)
        {
            await Assert.That(r.FileExtension).IsNotEmpty();
            await Assert.That(r.Content).IsNotEmpty();
        }
    }

    [Test]
    public async Task GetListingsByChapter_WithInvalidChapter_ReturnsEmptyList()
    {
        // Arrange
        using WebApplicationFactory factory = new();
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