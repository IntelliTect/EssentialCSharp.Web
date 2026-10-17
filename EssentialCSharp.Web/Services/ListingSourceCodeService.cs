using System.Globalization;
using System.Text.RegularExpressions;
using EssentialCSharp.Web.Models;
using Microsoft.Extensions.FileProviders;

namespace EssentialCSharp.Web.Services;

public partial class ListingSourceCodeService : IListingSourceCodeService, IDisposable
{
    private readonly IFileProvider _FileProvider;
    private readonly bool _OwnsFileProvider;
    private readonly string _ChapterDirectoryPrefix;
    private readonly ILogger<ListingSourceCodeService> _Logger;

    public ListingSourceCodeService(IWebHostEnvironment webHostEnvironment, ILogger<ListingSourceCodeService> logger, IConfiguration configuration)
    {
        _Logger = logger;

        string? listingSourceCodePath = configuration["LISTING_SOURCE_CODE_PATH"];
        if (!string.IsNullOrEmpty(listingSourceCodePath) && Directory.Exists(listingSourceCodePath))
        {
            _FileProvider = new PhysicalFileProvider(listingSourceCodePath);
            _OwnsFileProvider = true;
            _ChapterDirectoryPrefix = "src";
            LogUsingListingSourceCodeFrom(_Logger, listingSourceCodePath);
        }
        else
        {
            _FileProvider = webHostEnvironment.ContentRootFileProvider;
            _ChapterDirectoryPrefix = "ListingSourceCode/src";
        }
    }

    public void Dispose()
    {
        if (_OwnsFileProvider && _FileProvider is IDisposable disposable)
            disposable.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task<ListingSourceCodeResponse?> GetListingAsync(int chapterNumber, int listingNumber)
    {
        string chapterDirectory = $"{_ChapterDirectoryPrefix}/Chapter{chapterNumber:D2}";
        IDirectoryContents directoryContents = _FileProvider.GetDirectoryContents(chapterDirectory);

        if (!directoryContents.Exists)
        {
            LogChapterDirectoryNotFound(_Logger, chapterDirectory);
            return null;
        }

        Regex listingFileRegex = ListingFilePattern();
        IFileInfo? matchingFile = directoryContents
            .Where(f => !f.IsDirectory)
            .Select(f => new { File = f, Match = listingFileRegex.Match(f.Name) })
            .Where(x => x.Match.Success
                && int.Parse(x.Match.Groups[1].Value, CultureInfo.InvariantCulture) == chapterNumber
                && int.Parse(x.Match.Groups[2].Value, CultureInfo.InvariantCulture) == listingNumber)
            .OrderBy(x => string.Equals(Path.GetExtension(x.File.Name), ".cs", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(x => x.File.Name, StringComparer.Ordinal)
            .Select(x => x.File)
            .FirstOrDefault();

        if (matchingFile == null)
        {
            LogListingFileNotFound(_Logger, $"{chapterNumber:D2}.{listingNumber:D2}", chapterDirectory);
            return null;
        }

        string content = await ReadFileContentAsync(matchingFile);
        string extension = Path.GetExtension(matchingFile.Name).TrimStart('.');

        return new ListingSourceCodeResponse(
            chapterNumber,
            listingNumber,
            extension,
            content
        );
    }

    public async Task<IReadOnlyList<ListingSourceCodeResponse>> GetListingsByChapterAsync(int chapterNumber)
    {
        string chapterDirectory = $"{_ChapterDirectoryPrefix}/Chapter{chapterNumber:D2}";
        IDirectoryContents directoryContents = _FileProvider.GetDirectoryContents(chapterDirectory);

        if (!directoryContents.Exists)
        {
            LogChapterDirectoryNotFound(_Logger, chapterDirectory);
            return Array.Empty<ListingSourceCodeResponse>();
        }

        // Match both legacy "01.01.cs" and BookContent "Listing01.01.Title.cs" names.
        Regex listingFileRegex = ListingFilePattern();
        
        var matchedFiles = directoryContents
            .Where(f => !f.IsDirectory)
            .Select(f => new { File = f, Match = listingFileRegex.Match(f.Name) })
            .Where(x => x.Match.Success)
            .Select(x => new 
            { 
                x.File,
                ChapterNumber = int.Parse(x.Match.Groups[1].Value, CultureInfo.InvariantCulture),
                ListingNumber = int.Parse(x.Match.Groups[2].Value, CultureInfo.InvariantCulture),
                Extension = x.Match.Groups[3].Value
            })
            .Where(x => x.ChapterNumber == chapterNumber);

        var results = new List<ListingSourceCodeResponse>();
        
        foreach (var item in matchedFiles)
        {
            string content = await ReadFileContentAsync(item.File);
            
            results.Add(new ListingSourceCodeResponse(
                item.ChapterNumber,
                item.ListingNumber,
                item.Extension,
                content
            ));
        }

        return results.OrderBy(r => r.ListingNumber).ToList();
    }

    private static async Task<string> ReadFileContentAsync(IFileInfo file)
    {
        using Stream stream = file.CreateReadStream();
        using StreamReader reader = new(stream);
        return await reader.ReadToEndAsync();
    }

    [GeneratedRegex(@"^(?:Listing)?(\d{2})\.(\d{2})(?:\..+)?\.(\w+)$")]
    private static partial Regex ListingFilePattern();

    [LoggerMessage(Level = LogLevel.Information, Message = "Using listing source code from: {Path}")]
    private static partial void LogUsingListingSourceCodeFrom(ILogger<ListingSourceCodeService> logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Chapter directory not found: {ChapterDirectory}")]
    private static partial void LogChapterDirectoryNotFound(ILogger<ListingSourceCodeService> logger, string chapterDirectory);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Listing file not found: {Pattern} in {ChapterDirectory}")]
    private static partial void LogListingFileNotFound(ILogger<ListingSourceCodeService> logger, string pattern, string chapterDirectory);
}
