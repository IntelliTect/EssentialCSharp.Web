namespace EssentialCSharp.Web.Services;

public interface IGuidelinesService
{
    IReadOnlyList<GuidelineListing> Guidelines { get; }
}
