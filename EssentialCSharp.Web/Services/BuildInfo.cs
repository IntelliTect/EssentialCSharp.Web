using System.Reflection;

namespace EssentialCSharp.Web.Services;

/// <summary>
/// Identifies the build that is running, for <c>GET /version</c>. Deployment checks poll it to confirm
/// a new revision is serving traffic before smoke testing it. Nothing here is shown in the site UI.
/// </summary>
/// <param name="Version">The assembly's informational version, e.g. <c>1.0.0+&lt;sha&gt;</c>.</param>
/// <param name="Commit">The source revision (git SHA) the build came from, or <see langword="null"/> when the build was not stamped.</param>
public sealed record BuildInfo(string? Version, string? Commit)
{
    public static BuildInfo Current { get; } = FromAssembly(typeof(BuildInfo).Assembly);

    public static BuildInfo FromAssembly(Assembly assembly) =>
        FromInformationalVersion(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>
    /// The SDK appends <c>+$(SourceRevisionId)</c> to the informational version when a source revision is known
    /// (from SourceLink locally, or the <c>SOURCE_REVISION</c> Docker build argument in CI).
    /// </summary>
    public static BuildInfo FromInformationalVersion(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return new BuildInfo(null, null);
        }

        int plus = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        string? commit = plus >= 0 && plus < informationalVersion.Length - 1
            ? informationalVersion[(plus + 1)..]
            : null;

        return new BuildInfo(informationalVersion, commit);
    }
}
