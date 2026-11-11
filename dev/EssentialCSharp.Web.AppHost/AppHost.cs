using EssentialCSharp.Web.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// The ProjectReference that generates this metadata also builds Web before EF's --no-build update.
var webProjectPath = new Projects.EssentialCSharp_Web().ProjectPath;

// Tests set this so each run starts from fresh, disposable databases instead of the developer's volumes.
bool ephemeralDatabases = bool.TryParse(builder.Configuration["EssentialCSharpWeb:EphemeralDatabases"], out bool ephemeral) && ephemeral;

var sqlServer = builder.AddSqlServer("sqlserver");
var postgresServer = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector", "pg17");
if (!ephemeralDatabases)
{
    sqlServer.WithDataVolume().WithLifetime(ContainerLifetime.Persistent);
    postgresServer.WithDataVolume().WithLifetime(ContainerLifetime.Persistent);
}

var sql = sqlServer.AddDatabase("EssentialCSharpWebContextConnection");
var postgres = postgresServer.AddDatabase("PostgresVectorStore");

var feed = builder.AddEssentialCSharpContentFeedParameter();
builder.AddEssentialCSharpWeb(webProjectPath, sql, postgres)
    .WithEnvironment("AccessToNugetFeed", feed)
    .WithEssentialCSharpLocalAI();

builder.Build().Run();

