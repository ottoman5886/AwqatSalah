using Microsoft.Data.Sqlite;
using System.Collections.Concurrent;
using System.Text.Json;

namespace DiyanetNamazVakti.Api.Core.Caching;

/// <summary>
/// SQLite-basierter Cache – überlebt Neustarts und Deployments
/// Bei SQLite-Fehler automatischer Fallback auf Memory Cache
/// </summary>
public class SqliteCacheService : ICacheService
{
    private readonly ICacheSettings _cacheSettings;
    private readonly string _dbPath;
    private readonly ILogger<SqliteCacheService> _logger;
    private bool _sqliteAvailable = true;

    // Memory Fallback
    private readonly ConcurrentDictionary<string, (string Value, DateTime ExpiresAt)> _memoryCache = new();

    public SqliteCacheService(ICacheSettings cacheSettings, ILogger<SqliteCacheService> logger)
    {
        _cacheSettings = cacheSettings;
        _logger = logger;
        _dbPath = Path.Combine(AppContext.BaseDirectory, "cache", "prayertimes.db");
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        try
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
            _sqliteAvailable = true;
            _logger.LogInformation("[SQLite] Datenbank initialisiert: {Path}", _dbPath);
        }
        catch (Exception ex)
        {
            _sqliteAvailable = false;
            _logger.LogWarning(ex, "[SQLite] Nicht verfügbar – verwende Memory Cache als Fallback");
        }
    }

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> generatorAsync, DateTime expiredTime = default)
    {
        // Erst aus Cache lesen
        var cached = GetFromCache<T>(key);
        if (cached != null)
        {
            _logger.LogInformation("[Cache] Treffer: {Key}", key);
            return cached;
        }

        if (generatorAsync == null) return default;

        // Nicht im Cache → laden
        _logger.LogInformation("[Cache] Miss, lade Daten: {Key}", key);
        var result = await generatorAsync();

        if (result != null)
        {
            var expiry = expiredTime == default
                ? DateTime.Now.AddDays(_cacheSettings.ExpiryTime)
                : expiredTime;
            SaveToCache(key, result, expiry);
        }

        return result;
    }

    public bool Any(string key)
    {
        if (_sqliteAvailable)
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM Cache WHERE Key = $key AND ExpiresAt > $now";
                command.Parameters.AddWithValue("$key", key);
                command.Parameters.AddWithValue("$now", DateTime.Now.ToString("o"));
                return Convert.ToInt64(command.ExecuteScalar()) > 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SQLite] Fehler bei Any – Fallback auf Memory");
                _sqliteAvailable = false;
            }
        }

        return _memoryCache.TryGetValue(key, out var entry) && entry.ExpiresAt > DateTime.Now;
    }

    public void Remove(string key)
    {
        // Aus beiden entfernen
        _memoryCache.TryRemove(key, out _);

        if (!_sqliteAvailable) return;
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Cache WHERE Key = $key";
            command.Parameters.AddWithValue("$key", key);
            command.ExecuteNonQuery();
            _logger.LogInformation("[Cache] Gelöscht: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SQLite] Fehler bei Remove");
            _sqliteAvailable = false;
        }
    }

    public void Clear()
    {
        _memoryCache.Clear();

        if (!_sqliteAvailable) return;
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Cache";
            command.ExecuteNonQuery();
            _logger.LogInformation("[Cache] Komplett geleert");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SQLite] Fehler bei Clear");
            _sqliteAvailable = false;
        }
    }

    public void StartsWithClear(string prefix)
    {
        foreach (var key in _memoryCache.Keys.Where(k => k.StartsWith(prefix)))
            _memoryCache.TryRemove(key, out _);

        if (!_sqliteAvailable) return;
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Cache WHERE Key LIKE $prefix";
            command.Parameters.AddWithValue("$prefix", prefix + "%");
            command.ExecuteNonQuery();
            _logger.LogInformation("[Cache] Gelöscht mit Prefix: {Prefix}", prefix);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SQLite] Fehler bei StartsWithClear");
            _sqliteAvailable = false;
        }
    }

    public List<string> GetAllKeys()
    {
        if (_sqliteAvailable)
        {
            try
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
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SQLite] Fehler bei GetAllKeys – Fallback auf Memory");
                _sqliteAvailable = false;
            }
        }

        return _memoryCache
            .Where(x => x.Value.ExpiresAt > DateTime.Now)
            .Select(x => x.Key)
            .ToList();
    }

    public T Get<T>(string key) => GetFromCache<T>(key);

    public void Add(string key, object value)
    {
        if (value == null || Any(key)) return;
        SaveToCache(key, value, DateTime.Now.AddMinutes(_cacheSettings.ExpiryTime));
    }

    // ────────────────────────────────────────
    // Hilfsmethoden: SQLite + Memory Fallback
    // ────────────────────────────────────────

    private T GetFromCache<T>(string key)
    {
        // Erst SQLite versuchen
        if (_sqliteAvailable)
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
                if (value != null)
                    return JsonSerializer.Deserialize<T>(value)!;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SQLite] Fehler beim Lesen – Fallback auf Memory");
                _sqliteAvailable = false;
            }
        }

        // Memory Fallback
        if (_memoryCache.TryGetValue(key, out var entry) && entry.ExpiresAt > DateTime.Now)
        {
            _logger.LogInformation("[Memory] Cache-Treffer: {Key}", key);
            return JsonSerializer.Deserialize<T>(entry.Value)!;
        }

        return default;
    }

    private void SaveToCache(string key, object value, DateTime expiry)
    {
        var json = JsonSerializer.Serialize(value);

        // Immer in Memory speichern
        _memoryCache[key] = (json, expiry);

        // SQLite versuchen
        if (!_sqliteAvailable) return;
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT OR REPLACE INTO Cache (Key, Value, ExpiresAt)
                VALUES ($key, $value, $expiry)";
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", json);
            command.Parameters.AddWithValue("$expiry", expiry.ToString("o"));
            command.ExecuteNonQuery();
            _logger.LogInformation("[SQLite] Cache gespeichert: {Key}, läuft ab: {Expiry:yyyy-MM-dd HH:mm}", key, expiry);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SQLite] Fehler beim Speichern – nur in Memory gespeichert");
            _sqliteAvailable = false;
        }
    }
}
