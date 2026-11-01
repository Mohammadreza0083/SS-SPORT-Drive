using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SsSport.Drive.Application;
using SsSport.Drive.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// The <c>migrate</c> step, run with the owner's connection, also keeps the least-privilege application role
/// (decisions §1b): it may read and write rows in our and Hangfire's tables, and nothing else. Its password reaches
/// PostgreSQL only as a salted SCRAM-SHA-256 verifier.
/// </summary>
public sealed class DatabaseAppRoleTests : IAsyncLifetime
{
    /// <summary>The application role the tests ask for.</summary>
    private const string AppUser = "ssdrive_app";

    /// <summary>Its password in the tests.</summary>
    private const string AppPassword = "app-test-Password-0123456789";

    /// <summary>A throwaway PostgreSQL 17 server; keep the image in step with docker-compose.</summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>Starts PostgreSQL with an empty database.</summary>
    /// <returns>A task that completes when the server is ready.</returns>
    public async ValueTask InitializeAsync() => await _postgres.StartAsync();

    /// <summary>Stops and removes the PostgreSQL container.</summary>
    /// <returns>A task that completes when the container is gone.</returns>
    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();

    /// <summary>The app role can sign in and read and write rows in our tables and Hangfire's.</summary>
    [Fact]
    public async Task App_role_can_read_and_write_rows()
    {
        await MigrateAsync(AppUser, AppPassword);

        await using var connection = await OpenAsAsync(AppUser, AppPassword);
        (await ScalarAsync<long>(connection, "SELECT count(*) FROM countries")).Should().BePositive();
        (await ScalarAsync<bool>(connection,
            """
            SELECT bool_and(has_table_privilege(format('%I.%I', schemaname, tablename), 'SELECT, INSERT, UPDATE, DELETE'))
            FROM pg_tables WHERE schemaname IN ('public', 'hangfire')
            """)).Should().BeTrue();
        (await ScalarAsync<bool>(connection,
            """
            SELECT coalesce(bool_and(has_sequence_privilege(format('%I.%I', sequence_schema, sequence_name), 'USAGE, SELECT, UPDATE')), true)
            FROM information_schema.sequences WHERE sequence_schema IN ('public', 'hangfire')
            """)).Should().BeTrue();
    }

    /// <summary>The app role cannot change the schema: no creating, altering, dropping or truncating.</summary>
    /// <param name="sql">A statement only the owner may run.</param>
    [Theory]
    [InlineData("CREATE TABLE intruder (id int)")]
    [InlineData("CREATE TABLE hangfire.intruder (id int)")]
    [InlineData("DROP TABLE countries")]
    [InlineData("ALTER TABLE countries ADD COLUMN intruder int")]
    [InlineData("TRUNCATE countries")]
    [InlineData("CREATE ROLE intruder")]
    public async Task App_role_cannot_change_the_schema(string sql)
    {
        await MigrateAsync(AppUser, AppPassword);
        await using var connection = await OpenAsAsync(AppUser, AppPassword);

        var act = async () => await ExecuteAsync(connection, sql);

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    /// <summary>The role holds no special powers.</summary>
    [Fact]
    public async Task App_role_has_no_special_attributes()
    {
        await MigrateAsync(AppUser, AppPassword);
        await using var owner = await OpenOwnerAsync();

        (await ScalarAsync<bool>(owner,
            $"SELECT NOT (rolsuper OR rolcreaterole OR rolcreatedb OR rolreplication OR rolbypassrls) AND rolcanlogin FROM pg_roles WHERE rolname = '{AppUser}'"))
            .Should().BeTrue();
    }

    /// <summary>PostgreSQL stores only a salted SCRAM-SHA-256 verifier, never the password.</summary>
    [Fact]
    public async Task Password_is_stored_as_a_scram_verifier()
    {
        await MigrateAsync(AppUser, AppPassword);
        await using var owner = await OpenOwnerAsync();

        var stored = await ScalarAsync<string>(owner, $"SELECT rolpassword FROM pg_authid WHERE rolname = '{AppUser}'");

        stored.Should().StartWith("SCRAM-SHA-256$4096:").And.NotContain(AppPassword);
    }

    /// <summary>Running migrate again is safe and rotates the password: the old one stops working.</summary>
    [Fact]
    public async Task Migrate_again_rotates_the_password()
    {
        const string rotated = "rotated-Password-9876543210xyz";
        await MigrateAsync(AppUser, AppPassword);

        await MigrateAsync(AppUser, rotated);

        await using (var connection = await OpenAsAsync(AppUser, rotated))
        {
            (await ScalarAsync<int>(connection, "SELECT 1")).Should().Be(1);
        }

        var old = async () => await OpenAsAsync(AppUser, AppPassword);
        (await old.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InvalidPassword);
    }

    /// <summary>Without an app user configured, migrate creates no role.</summary>
    [Fact]
    public async Task No_app_user_creates_no_role()
    {
        await MigrateAsync(appUser: null, appPassword: null);
        await using var owner = await OpenOwnerAsync();

        (await ScalarAsync<long>(owner, $"SELECT count(*) FROM pg_roles WHERE rolname = '{AppUser}'")).Should().Be(0);
    }

    /// <summary>Unsafe or unusable settings are refused before anything is changed.</summary>
    /// <param name="appUser">The configured role name.</param>
    /// <param name="appPassword">The configured password.</param>
    [Theory]
    [InlineData("App-User", AppPassword)]
    [InlineData("app\"; DROP TABLE countries; --", AppPassword)]
    [InlineData("postgres", AppPassword)]
    [InlineData(AppUser, "")]
    [InlineData(AppUser, "short-pass")]
    public async Task Invalid_settings_are_refused(string appUser, string appPassword)
    {
        var act = async () => await MigrateAsync(appUser, appPassword);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await using var owner = await OpenOwnerAsync();
        (await ScalarAsync<bool>(owner, "SELECT to_regclass('public.countries') IS NULL")).Should().BeTrue("nothing is migrated with bad settings");
    }

    /// <summary>Runs the migrate step with the owner's connection and the given app-role settings.</summary>
    /// <param name="appUser">The app role name, or <see langword="null"/> for none.</param>
    /// <param name="appPassword">Its password.</param>
    /// <returns>A task that completes when migrate has finished.</returns>
    private async Task MigrateAsync(string? appUser, string? appPassword)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString(),
                ["Database:AppUser"] = appUser,
                ["Database:AppPassword"] = appPassword,
            })
            .Build();
        await using var provider = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddLogging()
            .AddApplication()
            .AddInfrastructure()
            .BuildServiceProvider();

        await DatabaseMigrator.MigrateAsync(provider, TestContext.Current.CancellationToken);
    }

    /// <summary>Opens a connection as the given role.</summary>
    /// <param name="user">The role.</param>
    /// <param name="password">Its password.</param>
    /// <returns>The open connection.</returns>
    private async Task<NpgsqlConnection> OpenAsAsync(string user, string password)
    {
        var builder = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Username = user, Password = password, Pooling = false };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }

    /// <summary>Opens a connection as the owner (the container's superuser).</summary>
    /// <returns>The open connection.</returns>
    private async Task<NpgsqlConnection> OpenOwnerAsync()
    {
        var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }

    /// <summary>Runs a statement.</summary>
    /// <param name="connection">The connection.</param>
    /// <param name="sql">The statement.</param>
    /// <returns>A task that completes when it has run.</returns>
    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Runs a query returning one value.</summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="connection">The connection.</param>
    /// <param name="sql">The query.</param>
    /// <returns>The value.</returns>
    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }
}
