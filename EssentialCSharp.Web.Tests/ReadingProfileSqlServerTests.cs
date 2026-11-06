using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Data;
using EssentialCSharp.Web.Models;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// Opt-in tests against a real SQL Server. Set ESSENTIALCSHARP_TEST_SQLSERVER to a connection string without a
/// database, for example
/// <c>Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true</c>.
/// Each test creates a throwaway database, applies the real migrations and drops the database afterwards.
/// </summary>
public class ReadingProfileSqlServerTests
{
    private const string EnvVar = "ESSENTIALCSHARP_TEST_SQLSERVER";
    private const string PageKey = "sql-test-page";

    private sealed class FakeWordCountService : IWordCountService
    {
        public bool IsReady => true;
        public PageWordStats? GetPageStats(string pageKey) => null;
        public int GetPageWordCount(string pageKey) => pageKey == PageKey ? 1000 : 0;
        public int GetBookWordCount() => 1000;
        public IReadOnlyList<ChapterWordCount> GetChapterWordCounts() => [];
    }

    private sealed class TestDatabase(string baseConnectionString, string name) : IAsyncDisposable
    {
        public string Name { get; } = name;

        public string ConnectionString =>
            new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = Name }.ConnectionString;

        // The EF model depends on Identity options from the application's service provider: AddDefaultIdentity
        // (Identity UI) sets Stores.MaxLengthForKeys = 128, which the migrations snapshot reflects. Mirror that here.
        private readonly ServiceProvider _services = new ServiceCollection()
            .AddDbContext<EssentialCSharpWebContext>(options =>
                // Same options as Program.cs, including the retrying execution strategy.
                options.UseSqlServer(
                    new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = name }.ConnectionString,
                    sql => sql.EnableRetryOnFailure(5)),
                ServiceLifetime.Transient)
            .AddIdentityCore<EssentialCSharpWebUser>(options => options.Stores.MaxLengthForKeys = 128)
            .AddEntityFrameworkStores<EssentialCSharpWebContext>()
            .Services
            .BuildServiceProvider();

        public EssentialCSharpWebContext CreateContext() => _services.GetRequiredService<EssentialCSharpWebContext>();

        public static ReadingProfileService CreateService(EssentialCSharpWebContext context) =>
            new(context, new FakeWordCountService(), Options.Create(new ReadingOptions()), NullLogger<ReadingProfileService>.Instance);

        public async ValueTask DisposeAsync()
        {
            await _services.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var master = new SqlConnection(baseConnectionString);
            await master.OpenAsync();
            await using SqlCommand command = master.CreateCommand();
            command.CommandText =
                $"IF DB_ID(N'{Name}') IS NOT NULL BEGIN ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Name}]; END";
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task<TestDatabase> CreateDatabaseAsync(bool migrate = true)
    {
        string baseConnectionString = Environment.GetEnvironmentVariable(EnvVar) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            Skip.Test($"Set {EnvVar} to a SQL Server connection string (no database), e.g. "
                + @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true");
        }

        var database = new TestDatabase(baseConnectionString, $"ReadingTest_{Guid.NewGuid():N}");
        if (migrate)
        {
            await using EssentialCSharpWebContext context = database.CreateContext();
            await context.Database.MigrateAsync();
        }

        return database;
    }

    private static async Task<string> CreateUserAsync(TestDatabase database)
    {
        string id = Guid.NewGuid().ToString();
        string name = $"sql-{id[..8]}";
        await using EssentialCSharpWebContext context = database.CreateContext();
        context.Users.Add(new EssentialCSharpWebUser
        {
            Id = id,
            UserName = name,
            NormalizedUserName = name.ToUpperInvariant(),
            Email = $"{name}@example.com",
            NormalizedEmail = $"{name.ToUpperInvariant()}@EXAMPLE.COM",
            SecurityStamp = Guid.NewGuid().ToString(),
        });
        await context.SaveChangesAsync();
        return id;
    }

    // 100 words in 60 seconds = 100 WPM: below the hard cutoff and not a slow outlier for any reference near 100.
    private static readonly ReadingInterval[] OneInterval = [new(PageKey, 100, 60)];

    private static async Task RecordAsync(TestDatabase database, string userId)
    {
        await using EssentialCSharpWebContext context = database.CreateContext();
        RecordSessionResult result = await TestDatabase.CreateService(context)
            .RecordSessionAsync(userId, OneInterval, CancellationToken.None);
        if (result.Profile is null)
        {
            throw new InvalidOperationException("Unexpected unknown page key.");
        }
    }

    private static async Task<(long Words, long Seconds)?> ReadTotalsAsync(TestDatabase database, string userId)
    {
        await using EssentialCSharpWebContext context = database.CreateContext();
        var row = await context.UserReadingProfiles.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new { p.TotalWordsRead, p.TotalActiveSeconds })
            .SingleOrDefaultAsync();
        return row is null ? null : (row.TotalWordsRead, row.TotalActiveSeconds);
    }

    [Test]
    public async Task Migrations_ApplyOnSqlServer_AndDownThenUpAgainWorks()
    {
        await using TestDatabase database = await CreateDatabaseAsync(migrate: false);
        await using EssentialCSharpWebContext context = database.CreateContext();
        IMigrator migrator = context.GetService<IMigrator>();

        await context.Database.MigrateAsync();
        bool tableAfterUp = await TableExistsAsync(context);
        IReadOnlyList<string> applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
        string previous = applied[^2];

        await migrator.MigrateAsync(previous);
        bool tableAfterDown = await TableExistsAsync(context);

        await context.Database.MigrateAsync();
        bool tableAfterReapply = await TableExistsAsync(context);

        using (Assert.Multiple())
        {
            await Assert.That(applied[^1]).EndsWith("AddUserReadingProfile");
            await Assert.That(tableAfterUp).IsTrue();
            await Assert.That(tableAfterDown).IsFalse();
            await Assert.That(tableAfterReapply).IsTrue();
            await Assert.That(await context.Database.GetPendingMigrationsAsync()).IsEmpty();
        }
    }

    private static async Task<bool> TableExistsAsync(EssentialCSharpWebContext context) =>
        await context.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'UserReadingProfiles'")
            .SingleAsync() == 1;

    [Test]
    public async Task FirstWriteCreatesRow_AndLaterWritesAccumulateExactly()
    {
        await using TestDatabase database = await CreateDatabaseAsync();
        string userId = await CreateUserAsync(database);

        (long, long)? before = await ReadTotalsAsync(database, userId);
        await RecordAsync(database, userId);
        (long, long)? afterFirst = await ReadTotalsAsync(database, userId);
        await RecordAsync(database, userId);
        await RecordAsync(database, userId);
        (long, long)? afterThree = await ReadTotalsAsync(database, userId);

        using (Assert.Multiple())
        {
            await Assert.That(before).IsNull();
            await Assert.That(afterFirst).IsEqualTo((100L, 60L));
            await Assert.That(afterThree).IsEqualTo((300L, 180L));
        }
    }

    [Test]
    public async Task ConcurrentWritesForSameNewUser_SumExactlyWithNoLostUpdatesOrErrors()
    {
        const int Concurrency = 50;
        await using TestDatabase database = await CreateDatabaseAsync();
        string userId = await CreateUserAsync(database);
        var gate = new TaskCompletionSource();

        Task[] calls = Enumerable.Range(0, Concurrency)
            .Select(_ => Task.Run(async () =>
            {
                await gate.Task;
                await RecordAsync(database, userId);
            }))
            .ToArray();
        gate.SetResult();
        await Task.WhenAll(calls);

        (long, long)? totals = await ReadTotalsAsync(database, userId);
        await Assert.That(totals).IsEqualTo((100L * Concurrency, 60L * Concurrency));
    }

    [Test]
    public async Task ConcurrentWritesForDifferentUsers_DoNotInterfere()
    {
        await using TestDatabase database = await CreateDatabaseAsync();
        string[] users = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => CreateUserAsync(database)));
        var gate = new TaskCompletionSource();

        // User i gets (i + 1) * 10 concurrent writes.
        Task[] calls = users
            .SelectMany((user, i) => Enumerable.Range(0, (i + 1) * 10).Select(_ => Task.Run(async () =>
            {
                await gate.Task;
                await RecordAsync(database, user);
            })))
            .ToArray();
        gate.SetResult();
        await Task.WhenAll(calls);

        for (int i = 0; i < users.Length; i++)
        {
            long writes = (i + 1) * 10;
            await Assert.That(await ReadTotalsAsync(database, users[i])).IsEqualTo((100L * writes, 60L * writes));
        }
    }

    [Test]
    public async Task DeletingUser_CascadesToTheirProfileRow()
    {
        await using TestDatabase database = await CreateDatabaseAsync();
        string userId = await CreateUserAsync(database);
        await RecordAsync(database, userId);

        await using (EssentialCSharpWebContext context = database.CreateContext())
        {
            await context.Users.Where(u => u.Id == userId).ExecuteDeleteAsync();
        }

        await Assert.That(await ReadTotalsAsync(database, userId)).IsNull();
    }

    [Test]
    public async Task Reset_RemovesOnlyTheCallersRow()
    {
        await using TestDatabase database = await CreateDatabaseAsync();
        string caller = await CreateUserAsync(database);
        string other = await CreateUserAsync(database);
        await RecordAsync(database, caller);
        await RecordAsync(database, other);

        await using (EssentialCSharpWebContext context = database.CreateContext())
        {
            await TestDatabase.CreateService(context).ResetAsync(caller, CancellationToken.None);
        }

        using (Assert.Multiple())
        {
            await Assert.That(await ReadTotalsAsync(database, caller)).IsNull();
            await Assert.That(await ReadTotalsAsync(database, other)).IsEqualTo((100L, 60L));
        }
    }
}
