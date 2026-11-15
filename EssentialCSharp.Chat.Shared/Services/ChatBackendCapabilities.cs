namespace EssentialCSharp.Chat.Common.Services;

/// <summary>
/// Describes what the selected <see cref="IChatCompletionService"/> backend can do, without
/// constructing it. Registered alongside the backend so UI code (for example the shared layout)
/// can read capabilities on every page render without building the backend's dependency graph.
/// </summary>
/// <param name="SupportsContextualSearch">Mirrors <see cref="IChatCompletionService.SupportsContextualSearch"/>.</param>
public sealed record ChatBackendCapabilities(bool SupportsContextualSearch);
