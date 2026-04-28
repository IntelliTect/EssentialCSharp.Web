namespace EssentialCSharp.Web.Data;

/// <summary>
/// Mirrors the ASP.NET Core Identity <c>AddDefaultIdentity</c> default for
/// <c>IdentityOptions.Stores.MaxLengthForKeys</c>.
/// EF model building does not derive that runtime option automatically, so the
/// schema contract keeps the explicit model configuration and runtime options aligned.
/// </summary>
public static class EssentialCSharpWebIdentitySchema
{
    /// <summary>
     /// ASP.NET Core Identity defaults login/token key lengths to 128.
    /// Source: https://github.com/dotnet/aspnetcore/blob/c4db2306aad327f8c45c546f82625082156f73bb/src/Identity/UI/src/IdentityServiceCollectionUIExtensions.cs#L48-L51
    /// Keep this in sync with <c>options.Stores.MaxLengthForKeys</c> and the explicit
    /// Identity model configuration so migrations continue to match the existing schema.
    /// </summary>
    public const int KeyMaxLength = 128;
}
