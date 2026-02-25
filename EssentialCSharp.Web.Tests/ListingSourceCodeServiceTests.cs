using EssentialCSharp.Web.Models;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Moq;
using Moq.AutoMock;

namespace EssentialCSharp.Web.Tests;

public class ListingSourceCodeServiceTests
{
    [Test]
    public async Task GetListingAsync_WithValidChapterAndListing_ReturnsCorrectListing()
    {
        // Arrange
        ListingSourceCodeService service = CreateService();

        // Act
        ListingSourceCodeResponse? result = await service.GetListingAsync(1, 1);

        // Assert
        await Assert.That(result).IsNotNull();
        using (Assert.Multiple())
        {
            await Assert.That(result.ChapterNumber).IsEqualTo(1);
            await Assert.That(result.ListingNumber).IsEqualTo(1);
            await Assert.That(result.FileExtension).IsEqualTo("cs");
            await Assert.That(result.Content).IsNotEmpty();
        }
    }

    [Test]
    public async Task GetListingAsync_WithInvalidChapter_ReturnsNull()
    {
        // Arrange
        ListingSourceCodeService service = CreateService();

        // Act
        ListingSourceCodeResponse? result = await service.GetListingAsync(999, 1);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetListingAsync_WithInvalidListing_ReturnsNull()
    {
        // Arrange
        ListingSourceCodeService service = CreateService();

        // Act
        ListingSourceCodeResponse? result = await service.GetListingAsync(1, 999);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetListingAsync_DifferentFileExtension_AutoDiscoversFileExtension()
    {
        // Arrange
        ListingSourceCodeService service = CreateService();

        // Act - Get an XML file (01.02.xml exists in Chapter 1)
        ListingSourceCodeResponse? result = await service.GetListingAsync(1, 2);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.FileExtension).IsEqualTo("xml");
    }

    [Test]
    public async Task GetListingsByChapterAsync_WithValidChapter_ReturnsAllListings()
    {
        // Arrange
        ListingSourceCodeService service = CreateService();

        // Act
        IReadOnlyList<ListingSourceCodeResponse> results = await service.GetListingsByChapterAsync(1);

        // Assert
        await Assert.That(results).IsNotEmpty();
        foreach (var r in results)
        {
            using (Assert.Multiple())
            {
                await Assert.That(r.ChapterNumber).IsEqualTo(1);
                await Assert.That(r.Content).IsNotEmpty();
                await Assert.That(r.FileExtension).IsNotEmpty();
            }
        }

        // Verify results are ordered
        await Assert.That(results).IsOrderedBy(r => r.ListingNumber);
    }

    [Test]
    public async Task GetListingsByChapterAsync_DirectoryContainsNonListingFiles_ExcludesNonListingFiles()
    {
        // Arrange - Chapter 10 has Employee.cs which doesn't match the pattern
        ListingSourceCodeService service = CreateService();

        // Act
        IReadOnlyList<ListingSourceCodeResponse> results = await service.GetListingsByChapterAsync(10);

        // Assert
        await Assert.That(results).IsNotEmpty();

        // Ensure all results match the {CC}.{LL}.{ext} pattern
        foreach (var r in results)
        {
            using (Assert.Multiple())
            {
                await Assert.That(r.ChapterNumber).IsEqualTo(10);
                await Assert.That(r.ListingNumber).IsBetween(1, 99);
            }
        }
    }

    [Test]
    public async Task GetListingsByChapterAsync_WithInvalidChapter_ReturnsEmptyList()
    {
        // Arrange
        ListingSourceCodeService service = CreateService();

        // Act
        IReadOnlyList<ListingSourceCodeResponse> results = await service.GetListingsByChapterAsync(999);

        // Assert
        await Assert.That(results).IsEmpty();
    }

    private static ListingSourceCodeService CreateService()
    {
        DirectoryInfo testDataRoot = GetTestDataPath();

        AutoMocker mocker = new();
        Mock<IWebHostEnvironment> mockWebHostEnvironment = mocker.GetMock<IWebHostEnvironment>();
        mockWebHostEnvironment.Setup(m => m.ContentRootPath).Returns(testDataRoot.FullName);
        mockWebHostEnvironment.Setup(m => m.ContentRootFileProvider).Returns(new PhysicalFileProvider(testDataRoot.FullName));

        return mocker.CreateInstance<ListingSourceCodeService>();
    }

    private static DirectoryInfo GetTestDataPath()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string testDataPath = Path.Join(baseDirectory, "TestData");
        
        DirectoryInfo testDataDirectory = new(testDataPath);
        
        if (!testDataDirectory.Exists)
        {
            throw new InvalidOperationException($"TestData directory not found at: {testDataDirectory.FullName}");
        }
        
        return testDataDirectory;
    }
}