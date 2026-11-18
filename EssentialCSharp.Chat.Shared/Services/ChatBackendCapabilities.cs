namespace EssentialCSharp.Chat.Common.Services;

// Registered next to the backend so the layout can read capabilities without constructing the backend.
public sealed record ChatBackendCapabilities(bool SupportsContextualSearch);
