using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Moq.AutoMock;

namespace EssentialCSharp.Web.Tests;

internal static class TestListingSourceCodeServiceHelper
{
    internal static string ResolveTestDataPath()
    {
        string testDataPath = Path.Join(AppContext.BaseDirectory, "TestData");
        if (!Directory.Exists(testDataPath))
            throw new InvalidOperationException($"TestData directory not found at: {testDataPath}");
        return testDataPath;
    }

    internal static ListingSourceCodeService CreateService()
    {
        string testDataPath = ResolveTestDataPath();

        AutoMocker mocker = new();
        mocker.Setup<IWebHostEnvironment, string>(m => m.ContentRootPath).Returns(testDataPath);
        mocker.Setup<IWebHostEnvironment, IFileProvider>(m => m.ContentRootFileProvider)
              .Returns(new PhysicalFileProvider(testDataPath));

        return mocker.CreateInstance<ListingSourceCodeService>();
    }
}
