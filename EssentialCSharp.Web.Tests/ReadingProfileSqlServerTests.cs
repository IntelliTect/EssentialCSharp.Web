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
/// The migration test uses its own throwaway database; the other tests share one migrated database per class
/// (each creates its own users, so they do not interfere) which is dropped after the class.
/// When the variable is missing the tests are skipped locally but fail when running in CI (<c>CI</c> or
/// <c>GITHUB_ACTIONS</c> set), so a misconfigured pipeline cannot silently stop covering SQL Server.
/// </summary>
public class ReadingProfileSqlServerTests
{
    private const string EnvVar = "ESSENTIALCSHARP_TEST_SQLSERVER";
    private const string PageKey = "sql-test-page";
    private const int PageWords = 1000;

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
            new(context, ReadingTestSupport.FakeWordCountService.WithSinglePage(PageKey, PageWords), Options.Create(ReadingTestSupport.CreateOptions()), NullLogger<ReadingProfileService>.Instance);

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

    private static string RequireConnectionString()
    {
        string connectionString = Environment.GetEnvironmentVariable(EnvVar) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        string message = $"Set {EnvVar} to a SQL Server connection string (no database), e.g. "
            + @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true";
        bool inCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        if (inCi)
        {
            throw new InvalidOperationException($"{EnvVar} must be set in CI so the SQL Server tests run. {message}");
        }

        Skip.Test(message);
        throw new InvalidOperationException("unreachable");
    }

    private static async Task<TestDatabase> CreateDatabaseAsync(bool migrate = true)
    {
        var database = new TestDatabase(RequireConnectionString(), $"ReadingTest_{Guid.NewGuid():N}");
        if (migrate)
        {
            await using EssentialCSharpWebContext context = database.CreateContext();
            await context.Database.MigrateAsync();
        }

        return database;
    }

    private static readonly SemaphoreSlim SharedDatabaseGate = new(1, 1);
    private static TestDatabase? _sharedDatabase;

    /// <summary>One migrated database for all tests in this class.</summary>
    private static async Task<TestDatabase> GetSharedDatabaseAsync()
    {
        await SharedDatabaseGate.WaitAsync();
        try
        {
            return _sharedDatabase ??= await CreateDatabaseAsync();
        }
        finally
        {
            SharedDatabaseGate.Release();
        }
    }

    [After(Class)]
    public static async Task DropSharedDatabaseAsync()
    {
        if (_sharedDatabase is { } database)
        {
            _sharedDatabase = null;
            await database.DisposeAsync();
        }
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
    public async Task UserReadingProfileMigration_ApplyingRevertingAndReapplyingWorksOnSqlServer()
    {
        await using TestDatabase database = await CreateDatabaseAsync(migrate: false);
        await using EssentialCSharpWebContext context = database.CreateContext();
        IMigrator migrator = context.GetService<IMigrator>();
        string[] all = context.Database.GetMigrations().ToArray();
        int index = Array.FindIndex(all, id => id.EndsWith("_AddUserReadingProfile", StringComparison.Ordinal));
        await Assert.That(index).IsGreaterThanOrEqualTo(0).Because("the AddUserReadingProfile migration must exist");
        string target = all[index];
        string predecessor = index == 0 ? Migration.InitialDatabase : all[index - 1];

        await migrator.MigrateAsync(target);
        bool tableAfterUp = await TableExistsAsync(context);

        await migrator.MigrateAsync(predecessor);
        bool tableAfterDown = await TableExistsAsync(context);

        await migrator.MigrateAsync(target);
        bool tableAfterReapply = await TableExistsAsync(context);

        await context.Database.MigrateAsync();
        string[] columns = await ColumnNamesAsync(context);

        using (Assert.Multiple())
        {
            await Assert.That(tableAfterUp).IsTrue();
            await Assert.That(tableAfterDown).IsFalse();
            await Assert.That(tableAfterReapply).IsTrue();
            await Assert.That(await context.Database.GetPendingMigrationsAsync()).IsEmpty();
            await Assert.That(context.Database.HasPendingModelChanges()).IsFalse()
                .Because("the migrations must fully describe the current model");
            await Assert.That(columns).IsEquivalentTo(new[]
            {
                nameof(UserReadingProfile.UserId),
                nameof(UserReadingProfile.TotalWordsRead),
                nameof(UserReadingProfile.TotalActiveSeconds),
                nameof(UserReadingProfile.UpdatedAtUtc),
            });
        }
    }

    private static async Task<string[]> ColumnNamesAsync(EssentialCSharpWebContext context) =>
        await context.Database
            .SqlQuery<string>($"SELECT COLUMN_NAME AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'UserReadingProfiles'")
            .ToArrayAsync();

    private static async Task<bool> TableExistsAsync(EssentialCSharpWebContext context) =>
        await context.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'UserReadingProfiles'")
            .SingleAsync() == 1;

    [Test]
    public async Task FirstWriteCreatesRow_AndLaterWritesAccumulateExactly()
    {
        TestDatabase database = await GetSharedDatabaseAsync();
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
    [Timeout(120_000)]
    public async Task ConcurrentWritesForSameNewUser_SumExactlyWithNoLostUpdatesOrErrors(CancellationToken cancellationToken)
    {
        const int Concurrency = 50;
        TestDatabase database = await GetSharedDatabaseAsync();
        string userId = await CreateUserAsync(database);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task[] calls = Enumerable.Range(0, Concurrency)
            .Select(_ => Task.Run(async () =>
            {
                await gate.Task;
                await RecordAsync(database, userId);
            }))
            .ToArray();
        gate.SetResult();
        await Task.WhenAll(calls).WaitAsync(cancellationToken);

        (long, long)? totals = await ReadTotalsAsync(database, userId);
        await Assert.That(totals).IsEqualTo((100L * Concurrency, 60L * Concurrency));
    }

    [Test]
    [Timeout(120_000)]
    public async Task ConcurrentWritesForSameExistingUser_SumExactlyWithNoLostUpdatesOrErrors(CancellationToken cancellationToken)
    {
        const int Concurrency = 50;
        TestDatabase database = await GetSharedDatabaseAsync();
        string userId = await CreateUserAsync(database);
        await RecordAsync(database, userId); // the row exists before the concurrent burst
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task[] calls = Enumerable.Range(0, Concurrency)
            .Select(_ => Task.Run(async () =>
            {
                await gate.Task;
                await RecordAsync(database, userId);
            }))
            .ToArray();
        gate.SetResult();
        await Task.WhenAll(calls).WaitAsync(cancellationToken);

        (long, long)? totals = await ReadTotalsAsync(database, userId);
        await Assert.That(totals).IsEqualTo((100L * (Concurrency + 1), 60L * (Concurrency + 1)));
    }

    [Test]
    public async Task ConcurrentWritesForDifferentUsers_DoNotInterfere()
    {
        TestDatabase database = await GetSharedDatabaseAsync();
        string[] users = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => CreateUserAsync(database)));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

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

        using (Assert.Multiple())
        {
            for (int i = 0; i < users.Length; i++)
            {
                long writes = (i + 1) * 10;
                await Assert.That(await ReadTotalsAsync(database, users[i])).IsEqualTo((100L * writes, 60L * writes));
            }
        }
    }

    [Test]
    public async Task WriteForANonexistentUser_FailsOnTheForeignKeyAndCreatesNoRow()
    {
        const int SqlServerForeignKeyViolation = 547;
        TestDatabase database = await GetSharedDatabaseAsync();
        string missingUserId = Guid.NewGuid().ToString();

        DbUpdateException? failure = null;
        try
        {
            await RecordAsync(database, missingUserId);
        }
        catch (DbUpdateException ex)
        {
            failure = ex;
        }

        using (Assert.Multiple())
        {
            await Assert.That(failure).IsNotNull();
            await Assert.That(failure?.InnerException).IsTypeOf<SqlException>();
            await Assert.That((failure?.InnerException as SqlException)?.Number).IsEqualTo(SqlServerForeignKeyViolation);
            await Assert.That(await ReadTotalsAsync(database, missingUserId)).IsNull();
        }
    }

    [Test]
    public async Task DeletingUser_CascadesToTheirProfileRow()
    {
        TestDatabase database = await GetSharedDatabaseAsync();
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
        TestDatabase database = await GetSharedDatabaseAsync();
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
