using EssentialCSharp.Web.Extensions;

namespace EssentialCSharp.Web.Services;

public sealed class GuidelinesService : IGuidelinesService
{
    private readonly IReadOnlyList<GuidelineListing> _guidelines;

    public GuidelinesService(IWebHostEnvironment environment, ILogger<GuidelinesService> logger)
    {
        FileInfo fileInfo = new(Path.Join(environment.ContentRootPath, "Guidelines", "guidelines.json"));
        _guidelines = fileInfo.ReadGuidelineJsonFromInputDirectory(logger) ?? [];
    }

    public IReadOnlyList<GuidelineListing> Guidelines => _guidelines;
}
