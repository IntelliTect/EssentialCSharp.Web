using System.Reflection;

namespace EssentialCSharp.Web.Services;

// Served by GET /version; deployment checks poll it. Commit is null when the build was not stamped.
public sealed record BuildInfo(string? Version, string? Commit)
{
    public static BuildInfo Current { get; } = FromAssembly(typeof(BuildInfo).Assembly);

    public static BuildInfo FromAssembly(Assembly assembly) =>
        FromInformationalVersion(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    // The SDK appends "+<SourceRevisionId>" (SourceLink locally, the SOURCE_REVISION build arg in CI).
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
