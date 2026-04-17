using System.Globalization;
using System.Text.RegularExpressions;
using EssentialCSharp.Web.Models;
using Microsoft.Extensions.FileProviders;

namespace EssentialCSharp.Web.Services;

public partial class ListingSourceCodeService : IListingSourceCodeService
{
    private readonly IFileProvider _FileProvider;
    private readonly string _ChapterDirectoryPrefix;
    private readonly ILogger<ListingSourceCodeService> _Logger;

    public ListingSourceCodeService(IWebHostEnvironment webHostEnvironment, ILogger<ListingSourceCodeService> logger, IConfiguration configuration)
    {
        _Logger = logger;

        string? listingSourceCodePath = configuration["LISTING_SOURCE_CODE_PATH"];
        if (!string.IsNullOrEmpty(listingSourceCodePath) && Directory.Exists(listingSourceCodePath))
        {
            _FileProvider = new PhysicalFileProvider(listingSourceCodePath);
            _ChapterDirectoryPrefix = "src";
            _Logger.LogInformation("Using listing source code from: {Path}", listingSourceCodePath);
        }
        else
        {
            _FileProvider = webHostEnvironment.ContentRootFileProvider;
            _ChapterDirectoryPrefix = "ListingSourceCode/src";
        }
    }

    public async Task<ListingSourceCodeResponse?> GetListingAsync(int chapterNumber, int listingNumber)
    {
        string chapterDirectory = $"{_ChapterDirectoryPrefix}/Chapter{chapterNumber:D2}";
        IDirectoryContents directoryContents = _FileProvider.GetDirectoryContents(chapterDirectory);

        if (!directoryContents.Exists)
        {
            _Logger.LogWarning("Chapter directory not found: {ChapterDirectory}", chapterDirectory);
            return null;
        }

        string pattern = $"{chapterNumber:D2}.{listingNumber:D2}.*";
        IFileInfo? matchingFile = directoryContents
            .Where(f => !f.IsDirectory)
            .FirstOrDefault(f => IsMatch(f.Name, pattern));

        if (matchingFile == null)
        {
            _Logger.LogWarning("Listing file not found: {Pattern} in {ChapterDirectory}", pattern, chapterDirectory);
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
            _Logger.LogWarning("Chapter directory not found: {ChapterDirectory}", chapterDirectory);
            return Array.Empty<ListingSourceCodeResponse>();
        }

        // Regex to match files like "01.01.cs" or "23.15.xml"
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

    private static bool IsMatch(string fileName, string pattern)
    {
        // Convert glob-like pattern to regex (simple version for our use case)
        string regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(fileName, regexPattern);
    }

    [GeneratedRegex(@"^(\d{2})\.(\d{2})\.(\w+)$")]
    private static partial Regex ListingFilePattern();
}
