using System.Text.RegularExpressions;
using Npgsql;
using SsSport.Drive.Infrastructure.BackgroundJobs;

namespace SsSport.Drive.Infrastructure.Persistence;

/// <summary>
/// The least-privilege role the API and workers connect with (section <c>Database</c>, decisions §1b). Empty
/// <see cref="AppUser"/> means none is managed (tests, a local <c>dotnet run</c> as the owner).
/// </summary>
public sealed class DatabaseAppRoleOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Database";

    /// <summary>The role's name: lower-case letters, digits and underscores, starting with a letter or underscore.</summary>
    public string? AppUser { get; set; }

    /// <summary>The role's password: printable ASCII, at least <see cref="DatabaseAppRole.MinimumPasswordLength"/> characters.</summary>
    public string? AppPassword { get; set; }
}

/// <summary>
/// Creates or updates the application role during <c>migrate</c>, which runs with the owner's connection. The role
/// may log in and read and write rows in our and Hangfire's tables (also tables created later), and nothing else:
/// no schema changes, no TRUNCATE, no role or database powers. Its password is sent as a salted SCRAM verifier, and
/// every name and value goes through PostgreSQL's <c>format()</c> as a parameter, never spliced into SQL here.
/// </summary>
public static partial class DatabaseAppRole
{
    /// <summary>The shortest accepted password.</summary>
    public const int MinimumPasswordLength = 16;

    /// <summary>The schemas whose rows the role may read and write.</summary>
    private static readonly string[] _schemas = ["public", HangfireStorage.Schema];

    /// <summary>
    /// Checks the settings before anything is changed.
    /// </summary>
    /// <param name="options">The settings.</param>
    /// <returns><see langword="true"/> when a role is configured; <see langword="false"/> when none is.</returns>
    /// <exception cref="InvalidOperationException">The name or the password is not acceptable.</exception>
    public static bool Validate(DatabaseAppRoleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrEmpty(options.AppUser))
        {
            return false;
        }

        if (!RoleName().IsMatch(options.AppUser) || options.AppUser.StartsWith("pg_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Database:AppUser must be lower-case letters, digits and underscores (at most 63), not starting with a digit or 'pg_'.");
        }

        if (options.AppPassword is not { Length: >= MinimumPasswordLength } password || !password.All(c => c is >= '!' and <= '~'))
        {
            throw new InvalidOperationException($"Database:AppPassword must be at least {MinimumPasswordLength} printable ASCII characters without spaces.");
        }

        return true;
    }

    /// <summary>Refuses a role name that is the owner's own; call it before migrating, so nothing is changed.</summary>
    /// <param name="dataSource">The database, connected as the owner.</param>
    /// <param name="options">Validated settings with a role configured.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>A task that completes when the check has passed.</returns>
    /// <exception cref="InvalidOperationException">The role name is the owner's own.</exception>
    public static async Task EnsureNotOwnerAsync(NpgsqlDataSource dataSource, DatabaseAppRoleOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        if (await ScalarAsync<bool>(connection, "SELECT current_user = @role", cancellationToken, options.AppUser))
        {
            throw new InvalidOperationException("Database:AppUser must not be the owner the migrations run as.");
        }
    }

    /// <summary>Creates the role or updates its password and attributes, then grants its privileges. Idempotent.</summary>
    /// <param name="dataSource">The database, connected as the owner.</param>
    /// <param name="options">Validated settings with a role configured.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the role is ready.</returns>
    public static async Task EnsureAsync(NpgsqlDataSource dataSource, DatabaseAppRoleOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        var role = options.AppUser!;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string attributes = "LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS";
        var exists = await ScalarAsync<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = @role)", cancellationToken, role);
        var verifier = ScramSha256.CreateVerifier(options.AppPassword!);
        await ExecuteFormattedAsync(connection, exists ? $"ALTER ROLE %I WITH {attributes} PASSWORD %L" : $"CREATE ROLE %I {attributes} PASSWORD %L", cancellationToken, role, verifier);

        var database = await ScalarAsync<string>(connection, "SELECT current_database()", cancellationToken);
        await ExecuteFormattedAsync(connection, "GRANT CONNECT ON DATABASE %I TO %I", cancellationToken, database, role);
        foreach (var schema in _schemas)
        {
            await ExecuteFormattedAsync(connection, "GRANT USAGE ON SCHEMA %I TO %I", cancellationToken, schema, role);
            await ExecuteFormattedAsync(connection, "GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO %I", cancellationToken, schema, role);
            await ExecuteFormattedAsync(connection, "GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA %I TO %I", cancellationToken, schema, role);
            // Tables and sequences later migrations create (as this owner) get the same grants.
            await ExecuteFormattedAsync(connection, "ALTER DEFAULT PRIVILEGES IN SCHEMA %I GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO %I", cancellationToken, schema, role);
            await ExecuteFormattedAsync(connection, "ALTER DEFAULT PRIVILEGES IN SCHEMA %I GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO %I", cancellationToken, schema, role);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Lets PostgreSQL build a statement with <c>format()</c> from parameters (<c>%I</c> quotes identifiers, <c>%L</c>
    /// literals), then runs it. Used for DDL, which cannot take bind parameters itself.
    /// </summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="template">The <c>format()</c> template; a constant.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <param name="arguments">The values for the template.</param>
    /// <returns>A task that completes when the statement has run.</returns>
    private static async Task ExecuteFormattedAsync(NpgsqlConnection connection, string template, CancellationToken cancellationToken, params string[] arguments)
    {
        await using var build = new NpgsqlCommand($"SELECT format(@template{string.Concat(arguments.Select((_, i) => $", @a{i}"))})", connection);
        build.Parameters.AddWithValue("template", template);
        for (var i = 0; i < arguments.Length; i++)
        {
            build.Parameters.AddWithValue($"a{i}", arguments[i]);
        }

        var statement = (string)(await build.ExecuteScalarAsync(cancellationToken))!;
        await using var run = new NpgsqlCommand(statement, connection);
        await run.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Runs a query returning one value, with an optional <c>@role</c> parameter.</summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="connection">The open connection.</param>
    /// <param name="sql">The query; a constant.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <param name="role">The value of <c>@role</c>, when the query uses it.</param>
    /// <returns>The value.</returns>
    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql, CancellationToken cancellationToken, string? role = null)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        if (role is not null)
        {
            command.Parameters.AddWithValue("role", role);
        }

        return (T)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>An unquoted PostgreSQL identifier of at most 63 characters.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex RoleName();
}
