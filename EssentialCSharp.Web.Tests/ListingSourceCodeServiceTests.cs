using EssentialCSharp.Web.Models;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Moq;

namespace EssentialCSharp.Web.Tests;

public class ListingSourceCodeServiceTests
{
    [Fact]
    public async Task GetListingAsync_WithValidChapterAndListing_ReturnsCorrectListing()
    {
        // Arrange
        ListingSourceCodeService service = CreateService();

        // Act
        ListingSourceCodeResponse? result = await service.GetListingAsync(1, 1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.ChapterNumber);
        Assert.Equal(1, result.ListingNumber);
        Assert.Equal("cs", result.FileExtension);
        Assert.NotEmpty(result.Content);
    }

    [Fact]
    public async Task GetListingAsync_WithInvalidChapter_ReturnsNull()
    {
        // Arrange
        ListingSourceCodeService service = CreateService();

        // Act
        ListingSourceCodeResponse? result = await service.GetListingAsync(999, 1);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetListingAsync_WithInvalidListing_ReturnsNull()
    {
        // Arrange
        ListingSourceCodeService service = CreateService();

        // Act
        ListingSourceCodeResponse? result = await service.GetListingAsync(1, 999);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetListingAsync_DifferentFileExtension_AutoDiscoversFileExtension()
    {
        // Arrange
        ListingSourceCodeService service = CreateService();

        // Act - Get an XML file (01.02.xml exists in Chapter 1)
        ListingSourceCodeResponse? result = await service.GetListingAsync(1, 2);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("xml", result.FileExtension);
    }

    [Fact]
    public async Task GetListingsByChapterAsync_WithValidChapter_ReturnsAllListings()
    {
        // Arrange
        ListingSourceCodeService service = CreateService();

        // Act
        IReadOnlyList<ListingSourceCodeResponse> results = await service.GetListingsByChapterAsync(1);

        // Assert
        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.Equal(1, r.ChapterNumber));
        Assert.All(results, r => Assert.NotEmpty(r.Content));
        Assert.All(results, r => Assert.NotEmpty(r.FileExtension));
        
        // Verify results are ordered
        Assert.Equal(results.OrderBy(r => r.ListingNumber).ToList(), results);
    }

    [Fact]
    public async Task GetListingsByChapterAsync_DirectoryContainsNonListingFiles_ExcludesNonListingFiles()
    {
        // Arrange - Chapter 10 has Employee.cs which doesn't match the pattern
        ListingSourceCodeService service = CreateService();

        // Act
        IReadOnlyList<ListingSourceCodeResponse> results = await service.GetListingsByChapterAsync(10);

        // Assert
        Assert.NotEmpty(results);
        
        // Ensure all results match the {CC}.{LL}.{ext} pattern
        Assert.All(results, r => 
        {
            Assert.Equal(10, r.ChapterNumber);
            Assert.InRange(r.ListingNumber, 1, 99);
        });
    }

    [Fact]
    public async Task GetListingsByChapterAsync_WithInvalidChapter_ReturnsEmptyList()
    {
        // Arrange
        ListingSourceCodeService service = CreateService();

        // Act
        IReadOnlyList<ListingSourceCodeResponse> results = await service.GetListingsByChapterAsync(999);

        // Assert
        Assert.Empty(results);
    }

    private static ListingSourceCodeService CreateService()
    {
        string testDataRoot = GetTestDataPath();
        
        var mockWebHostEnvironment = new Mock<IWebHostEnvironment>();
        mockWebHostEnvironment.Setup(m => m.ContentRootPath).Returns(testDataRoot);
        mockWebHostEnvironment.Setup(m => m.ContentRootFileProvider).Returns(new PhysicalFileProvider(testDataRoot));
        
        var mockLogger = new Mock<ILogger<ListingSourceCodeService>>();
        
        return new ListingSourceCodeService(mockWebHostEnvironment.Object, mockLogger.Object);
    }

    private static string GetTestDataPath()
    {
        // Get the test project directory and navigate to TestData folder
        string currentDirectory = Directory.GetCurrentDirectory();
        string testDataPath = Path.Combine(currentDirectory, "TestData");
        
        if (!Directory.Exists(testDataPath))
        {
            throw new InvalidOperationException($"TestData directory not found at: {testDataPath}");
        }
        
        return testDataPath;
    }
}
