using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Quotebook.Data;

namespace Quotebook.Tests;

/// <summary>
/// The database a test runs against. By default an in-memory one, which is fast but does not enforce
/// foreign keys, unique indexes or the xmin version column. Set TEST_POSTGRES to a connection string and
/// the same tests run on real PostgreSQL instead, each test class in its own throwaway "qb_test_*" schema
/// that is dropped afterwards.
///
/// Point TEST_POSTGRES at a throwaway server (CI uses a service container), never at real data. As a
/// backstop the constructor checks that the schema switch took effect before it writes anything, and
/// refuses to continue if it did not.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    public static readonly string? Connection = Environment.GetEnvironmentVariable("TEST_POSTGRES");
    public static bool UsePostgres => !string.IsNullOrWhiteSpace(Connection);

    readonly string name = Guid.NewGuid().ToString("N");
    string Schema => "qb_test_" + name;

    public TestDatabase()
    {
        if (!UsePostgres) return;
        Run($"CREATE SCHEMA \"{Schema}\"");

        using var db = new QuotebookDbContext(Options, new NoTenant());
        var current = db.Database.SqlQueryRaw<string>("select current_schema() as \"Value\"").AsEnumerable().Single();
        if (current != Schema)
        {
            Dispose();
            throw new InvalidOperationException($"Expected to work in schema {Schema} but the connection is in '{current}'. Refusing to run tests against it.");
        }

        db.Database.Migrate(); // the real migrations, so the real constraints
    }

    public DbContextOptions<QuotebookDbContext> Options
    {
        get
        {
            var builder = new DbContextOptionsBuilder<QuotebookDbContext>();
            Configure(builder);
            return builder.Options;
        }
    }

    public DbContextOptionsBuilder Configure(DbContextOptionsBuilder o)
        => UsePostgres
            ? o.UseNpgsql(PooledConnection(), n => n.EnableRetryOnFailure(4, TimeSpan.FromSeconds(5), null))
               .AddInterceptors(new SearchPath(Schema))
            : o.UseInMemoryDatabase(name);

    static string PooledConnection()
        => new NpgsqlConnectionStringBuilder(Connection) { MaxPoolSize = 4, MinPoolSize = 0 }.ConnectionString;

    public void Dispose()
    {
        if (!UsePostgres) return;
        Run($"DROP SCHEMA IF EXISTS \"{Schema}\" CASCADE");
    }

    static void Run(string sql)
    {
        using var connection = new NpgsqlConnection(Connection);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }

    /// <summary>Points every connection EF opens at this test's schema (a connection-string setting is ignored by some hosts).</summary>
    sealed class SearchPath(string schema) : DbConnectionInterceptor
    {
        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
            => Set(connection);

        public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SET search_path TO \"{schema}\"";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        void Set(DbConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SET search_path TO \"{schema}\"";
            command.ExecuteNonQuery();
        }
    }
}
