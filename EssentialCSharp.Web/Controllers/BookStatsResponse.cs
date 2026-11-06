namespace EssentialCSharp.Web.Controllers;

public sealed record ChapterStatsResponse(int ChapterNumber, int WordCount);

public sealed record BookStatsResponse(int TotalWordCount, IReadOnlyList<ChapterStatsResponse> Chapters);
