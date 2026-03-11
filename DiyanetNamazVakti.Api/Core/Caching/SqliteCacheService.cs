using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace DiyanetNamazVakti.Api.Core.Caching;

/// <summary>
/// SQLite-basierter Cache – überlebt Neustarts und Deployments
/// </summary>
public class SqliteCacheService : ICacheService
{
    private readonly ICacheSettings _cacheSettings;
    private readonly string _dbPath;
    private readonly ILogger<SqliteCacheService> _logger;

    public SqliteCacheService(ICacheSettings cacheSettings, ILogger<SqliteCacheService> logger)
    {
        _cacheSettings = cacheSettings;
        _logger = logger;
        _dbPath = Path.Combine(AppContext.BaseDirectory, "cache", "prayertimes.db");
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS Cache (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL,
                ExpiresAt TEXT NOT NULL
            )";
        command.ExecuteNonQuery();
        _logger.LogInformation("[SQLite] Datenbank initialisiert: {Path}", _dbPath);
    }

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> generatorAsync, DateTime expiredTime = default)
    {
        // Erst aus SQLite lesen
        var cached = GetFromDb<T>(key);
        if (cached != null)
        {
            _logger.LogInformation("[SQLite] Cache-Treffer: {Key}", key);
            return cached;
        }

        // Nicht im Cache → von Diyanet laden
        _logger.LogInformation("[SQLite] Cache-Miss, lade von Diyanet: {Key}", key);
        var result = await generatorAsync();

        if (result != null)
        {
            var expiry = expiredTime == default
                ? DateTime.Now.AddDays(_cacheSettings.ExpiryTime)
                : expiredTime;
            SaveToDb(key, result, expiry);
        }

        return result;
    }

    public bool Any(string key)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Cache WHERE Key = $key AND ExpiresAt > $now";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$now", DateTime.Now.ToString("o"));
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    public void Remove(string key)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Cache WHERE Key = $key";
        command.Parameters.AddWithValue("$key", key);
        command.ExecuteNonQuery();
        _logger.LogInformation("[SQLite] Cache gelöscht: {Key}", key);
    }

    public void Clear()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Cache";
        command.ExecuteNonQuery();
        _logger.LogInformation("[SQLite] Cache komplett geleert");
    }

    public void StartsWithClear(string prefix)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Cache WHERE Key LIKE $prefix";
        command.Parameters.AddWithValue("$prefix", prefix + "%");
        command.ExecuteNonQuery();
        _logger.LogInformation("[SQLite] Cache gelöscht mit Prefix: {Prefix}", prefix);
    }

    public List<string> GetAllKeys()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Key FROM Cache WHERE ExpiresAt > $now";
        command.Parameters.AddWithValue("$now", DateTime.Now.ToString("o"));
        var keys = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            keys.Add(reader.GetString(0));
        return keys;
    }

    public T Get<T>(string key)
    {
        return GetFromDb<T>(key);
    }

    public void Add(string key, object value)
    {
        if (value == null || Any(key)) return;
        SaveToDb(key, value, DateTime.Now.AddMinutes(_cacheSettings.ExpiryTime));
    }

    // ────────────────────────────────────────
    // Hilfsmethoden
    // ────────────────────────────────────────

    private T GetFromDb<T>(string key)
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Value FROM Cache WHERE Key = $key AND ExpiresAt > $now";
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$now", DateTime.Now.ToString("o"));
            var value = command.ExecuteScalar() as string;
            if (value == null) return default;
            return JsonSerializer.Deserialize<T>(value)!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SQLite] Fehler beim Lesen: {Key}", key);
            return default;
        }
    }

    private void SaveToDb(string key, object value, DateTime expiry)
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT OR REPLACE INTO Cache (Key, Value, ExpiresAt)
                VALUES ($key, $value, $expiry)";
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(value));
            command.Parameters.AddWithValue("$expiry", expiry.ToString("o"));
            command.ExecuteNonQuery();
            _logger.LogInformation("[SQLite] Cache gespeichert: {Key}, läuft ab: {Expiry:yyyy-MM-dd HH:mm}", key, expiry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SQLite] Fehler beim Speichern: {Key}", key);
        }
    }
}
