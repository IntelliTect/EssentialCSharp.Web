namespace EssentialCSharp.Web.Models;

public static class AnnouncementCatalog
{
    public static IReadOnlyList<AnnouncementCard> WebFeaturesComingSoon { get; } =
    [
        new("Hyperlinking", "Easily navigate to interesting and relevant sites as well as related sections in Essential C#.")
    ];

    public static IReadOnlyList<AnnouncementCard> ContentComingSoon { get; } =
    [
        new("Experimental attribute", "New feature from C# 12.0."),
        new("Source Generators", "A newer .NET feature."),
        new("C# 13.0 Features", "Various new features coming in C# 13.0.")
    ];

    public static IReadOnlyList<AnnouncementCard> RecentlyCompleted { get; } =
    [
        new("AI Chat Assistant", "Chat with an AI assistant that has access to Essential C# book content from a floating widget available on every page. Supports streaming responses, markdown rendering, and saved conversation history. Requires sign-in."),
        new("Client-side Compiler", "Write, compile, and run C# code snippets right from your browser using the integrated Try .NET editor."),
        new("Interactive Code Listings", "Edit, compile, and run the code listings found throughout Essential C#. Runnable listings show a Run button that opens an interactive editor."),
        new("Copying Header Hyperlinks", "Easily copy a header URL to link to a book section."),
        new("Home Page", "Add a home page that features a short description of the book and a high level mindmap."),
        new("Keyboard Shortcuts", "Quickly navigate through the book via keyboard shortcuts (right/left arrows, 'n', 'p').")
    ];
}
