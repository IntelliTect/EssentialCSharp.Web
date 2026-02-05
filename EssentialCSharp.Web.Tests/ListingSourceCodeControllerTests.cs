using System.Net;
using System.Net.Http.Json;
using EssentialCSharp.Web.Models;

namespace EssentialCSharp.Web.Tests;

public class ListingSourceCodeControllerTests
{
    [Fact]
    public async Task GetListing_WithValidChapterAndListing_Returns200WithContent()
    {
        // Arrange
        using WebApplicationFactory factory = new();
        HttpClient client = factory.CreateClient();

        // Act
        using HttpResponseMessage response = await client.GetAsync("/api/ListingSourceCode/1/1");
        
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        ListingSourceCodeResponse? result = await response.Content.ReadFromJsonAsync<ListingSourceCodeResponse>();
        Assert.NotNull(result);
        Assert.Equal(1, result.ChapterNumber);
        Assert.Equal(1, result.ListingNumber);
        Assert.NotEmpty(result.FileExtension);
        Assert.NotEmpty(result.Content);
    }


    [Fact]
    public async Task GetListing_WithInvalidChapter_Returns404()
    {
        // Arrange
        using WebApplicationFactory factory = new();
        HttpClient client = factory.CreateClient();

        // Act
        using HttpResponseMessage response = await client.GetAsync("/api/ListingSourceCode/999/1");
        
        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetListing_WithInvalidListing_Returns404()
    {
        // Arrange
        using WebApplicationFactory factory = new();
        HttpClient client = factory.CreateClient();

        // Act
        using HttpResponseMessage response = await client.GetAsync("/api/ListingSourceCode/1/999");
        
        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetListingsByChapter_WithValidChapter_ReturnsMultipleListings()
    {
        // Arrange
        using WebApplicationFactory factory = new();
        HttpClient client = factory.CreateClient();

        // Act
        using HttpResponseMessage response = await client.GetAsync("/api/ListingSourceCode/1");
        
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        List<ListingSourceCodeResponse>? results = await response.Content.ReadFromJsonAsync<List<ListingSourceCodeResponse>>();
        Assert.NotNull(results);
        Assert.NotEmpty(results);
        
        // Verify all results are from chapter 1
        Assert.All(results, r => Assert.Equal(1, r.ChapterNumber));
        
        // Verify results are ordered by listing number
        Assert.Equal(results.OrderBy(r => r.ListingNumber).ToList(), results);
        
        // Verify each listing has required properties
        Assert.All(results, r => 
        {
            Assert.NotEmpty(r.FileExtension);
            Assert.NotEmpty(r.Content);
        });
    }

    [Fact]
    public async Task GetListingsByChapter_WithInvalidChapter_ReturnsEmptyList()
    {
        // Arrange
        using WebApplicationFactory factory = new();
        HttpClient client = factory.CreateClient();

        // Act
        using HttpResponseMessage response = await client.GetAsync("/api/ListingSourceCode/999");
        
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        List<ListingSourceCodeResponse>? results = await response.Content.ReadFromJsonAsync<List<ListingSourceCodeResponse>>();
        Assert.NotNull(results);
        Assert.Empty(results);
    }
}
