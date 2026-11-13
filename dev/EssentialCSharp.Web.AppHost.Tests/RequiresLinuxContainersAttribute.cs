using System.Diagnostics;

namespace EssentialCSharp.Web.AppHost.Tests;

/// <summary>
/// Skips tests that start Linux containers when no Linux container engine is reachable
/// (for example hosted macOS runners, or Windows runners that only provide Windows containers).
/// Linux CI never skips, so a missing engine there still fails the build.
/// </summary>
public sealed class RequiresLinuxContainersAttribute()
    : SkipAttribute("Requires a running Docker-compatible engine that serves Linux containers.")
{
    private static readonly Lazy<bool> s_available = new(IsLinuxContainerEngineAvailable);

    public override Task<bool> ShouldSkip(TestRegisteredContext context)
    {
        if (OperatingSystem.IsLinux() && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(!s_available.Value);
    }

    private static bool IsLinuxContainerEngineAvailable()
    {
        string runtime = Environment.GetEnvironmentVariable("ASPIRE_CONTAINER_RUNTIME") ?? "docker";
        try
        {
            using Process process = Process.Start(new ProcessStartInfo(runtime, ["version", "--format", "{{.Server.Os}}"])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            })!;

            Task<string> output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeSpan.FromSeconds(15)))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            return process.ExitCode == 0 && output.Result.Trim().Equals("linux", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
