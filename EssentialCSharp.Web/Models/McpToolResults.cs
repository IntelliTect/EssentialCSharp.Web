namespace EssentialCSharp.Web.Models;

public sealed record BookTocItemResult(
    string Key,
    string Title,
    string Href,
    string Url,
    int Level,
    IReadOnlyList<BookTocItemResult> Items);

public sealed record ChapterListToolResult(
    string Title,
    IReadOnlyList<BookTocItemResult> Chapters);

public sealed record BookSectionReferenceResult(
    string Key,
    string Heading,
    int ChapterNumber,
    string ChapterTitle,
    int IndentLevel,
    string? AnchorId,
    string Href,
    string Url);

public sealed record ChapterSectionsToolResult(
    int ChapterNumber,
    string ChapterTitle,
    IReadOnlyList<BookSectionReferenceResult> Sections);

public sealed record NavigationContextToolResult(
    BookSectionReferenceResult Section,
    IReadOnlyList<BookSectionReferenceResult> Breadcrumb,
    BookSectionReferenceResult? Parent,
    BookSectionReferenceResult? Previous,
    BookSectionReferenceResult? Next,
    IReadOnlyList<BookSectionReferenceResult> Siblings);

public sealed record BookGuidelineSummaryResult(
    string Type,
    string Guideline,
    int ChapterNumber,
    string ChapterTitle,
    string Subsection);

public sealed record BookContentExcerptResult(
    int? ChapterNumber,
    string? Heading,
    string ChunkText);

public sealed record ChapterSummaryToolResult(
    int ChapterNumber,
    string ChapterTitle,
    IReadOnlyList<BookSectionReferenceResult> Sections,
    IReadOnlyList<BookGuidelineSummaryResult> Guidelines);

public sealed record DiagnosticHelpToolResult(
    string Diagnostic,
    string? SearchTerm,
    IReadOnlyList<BookSectionReferenceResult> RelevantSections,
    IReadOnlyList<BookContentExcerptResult> RelevantBookContent,
    IReadOnlyList<BookGuidelineSummaryResult> RelatedGuidelines,
    bool SemanticSearchAvailable);

public sealed record ListingSourceCodeResult(
    int ChapterNumber,
    int ListingNumber,
    string LanguageHint,
    string Content);

public sealed record ListingSearchToolResult(
    string Pattern,
    IReadOnlyList<ListingSourceCodeResult> Matches);
