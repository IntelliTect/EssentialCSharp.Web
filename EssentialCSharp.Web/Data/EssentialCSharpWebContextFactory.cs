using EssentialCSharp.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace EssentialCSharp.Web.Data;

public sealed class EssentialCSharpWebContextFactory : IDesignTimeDbContextFactory<EssentialCSharpWebContext>
{
    private const string ConnectionStringName = "EssentialCSharpWebContextConnection";
    private const string FallbackConnectionString =
        "Server=localhost;Database=EssentialCSharp.Web.DesignTime;User Id=sa;Password=NotUsed123!;TrustServerCertificate=true;";

    public EssentialCSharpWebContext CreateDbContext(string[] args)
    {
        string environmentName =
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? "Production";

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(ResolveProjectPath())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true)
            .AddUserSecrets<Program>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        string? configuredConnectionString = configuration.GetConnectionString(ConnectionStringName);
        string connectionString = string.IsNullOrWhiteSpace(configuredConnectionString)
            ? FallbackConnectionString
            : configuredConnectionString;

        DbContextOptionsBuilder<EssentialCSharpWebContext> options = new();
        options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(5));

        return new EssentialCSharpWebContext(options.Options);
    }

    private static string ResolveProjectPath()
    {
        string currentDirectory = Directory.GetCurrentDirectory();
        if (File.Exists(Path.Combine(currentDirectory, "appsettings.json")))
        {
            return currentDirectory;
        }

        string childProjectDirectory = Path.Combine(currentDirectory, "EssentialCSharp.Web");
        if (File.Exists(Path.Combine(childProjectDirectory, "appsettings.json")))
        {
            return childProjectDirectory;
        }

        string? parentDirectory = Directory.GetParent(currentDirectory)?.FullName;
        if (parentDirectory is not null)
        {
            string siblingProjectDirectory = Path.Combine(parentDirectory, "EssentialCSharp.Web");
            if (File.Exists(Path.Combine(siblingProjectDirectory, "appsettings.json")))
            {
                return siblingProjectDirectory;
            }
        }

        return currentDirectory;
    }
}
