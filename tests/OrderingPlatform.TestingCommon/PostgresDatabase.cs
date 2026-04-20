using Npgsql;
using Respawn;

namespace OrderingPlatform.TestingCommon;

/// <summary>
/// One-per-test-class Postgres database. Creates a fresh database on <see cref="InitializeAsync"/>,
/// runs caller-supplied migrations, and deletes the database on <see cref="DisposeAsync"/>.
/// Use <see cref="ResetAsync"/> between tests to truncate tables without losing schema.
/// </summary>
public class PostgresDatabase : IAsyncLifetime
{
    private readonly string _databaseName;
    private readonly Func<string, Task> _applyMigrations;
    private Respawner? _respawner;
    private NpgsqlConnection? _resetConnection;

    public string ConnectionString { get; }

    public PostgresDatabase(string databaseName, Func<string, Task> applyMigrations)
    {
        _databaseName = databaseName;
        _applyMigrations = applyMigrations;
        ConnectionString =
            $"Host={TestEnvironment.PostgresHost};Port=5432;Database={databaseName};Username=postgres;Password=postgres;Include Error Detail=true";
    }

    public async Task InitializeAsync()
    {
        await using (var admin = new NpgsqlConnection(TestEnvironment.PostgresAdminConnectionString))
        {
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE);", admin);
            await drop.ExecuteNonQueryAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\";", admin);
            await create.ExecuteNonQueryAsync();
        }

        await _applyMigrations(ConnectionString);

        _resetConnection = new NpgsqlConnection(ConnectionString);
        await _resetConnection.OpenAsync();
        try
        {
            _respawner = await Respawner.CreateAsync(_resetConnection, new RespawnerOptions
            {
                DbAdapter = DbAdapter.Postgres,
                SchemasToInclude = ["public"]
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("No tables found"))
        {
            // Empty schema — ResetAsync becomes a no-op until the domain adds tables.
            _respawner = null;
        }
    }

    public async Task ResetAsync()
    {
        if (_resetConnection is null)
        {
            throw new InvalidOperationException("PostgresDatabase.ResetAsync called before InitializeAsync.");
        }
        if (_respawner is null)
        {
            return;
        }
        await _respawner.ResetAsync(_resetConnection);
    }

    public async Task DisposeAsync()
    {
        if (_resetConnection is not null)
        {
            await _resetConnection.DisposeAsync();
        }

        NpgsqlConnection.ClearAllPools();

        await using var admin = new NpgsqlConnection(TestEnvironment.PostgresAdminConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE);", admin);
        await drop.ExecuteNonQueryAsync();
    }
}
