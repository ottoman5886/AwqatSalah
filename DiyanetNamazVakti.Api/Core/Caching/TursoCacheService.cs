using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;

namespace DiyanetNamazVakti.Api.Core.Caching;

/// <summary>
/// Turso-basierter Cache – persistent, überlebt alle Neustarts und Deployments
/// Bei Turso-Fehler automatischer Fallback auf Memory Cache
/// </summary>
public class TursoCacheService : ICacheService
{
    private readonly ICacheSettings _cacheSettings;
    private readonly ILogger<TursoCacheService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private bool _tursoAvailable = true;

    // Memory Fallback
    private readonly ConcurrentDictionary<string, (string Value, DateTime ExpiresAt)> _memoryCache = new();

    public TursoCacheService(ICacheSettings cacheSettings, ILogger<TursoCacheService> logger, IConfiguration configuration)
    {
        _cacheSettings = cacheSettings;
        _logger = logger;

        var tursoUrl = configuration["TursoSettings:DatabaseUrl"]!;
        var tursoToken = configuration["TursoSettings:AuthToken"]!;

        _baseUrl = tursoUrl.TrimEnd('/');
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tursoToken);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        InitializeDatabase().GetAwaiter().GetResult();
    }

    private async Task InitializeDatabase()
    {
        try
        {
            await ExecuteSql(@"CREATE TABLE IF NOT EXISTS Cache (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL,
                ExpiresAt TEXT NOT NULL
            )");
            _tursoAvailable = true;
            _logger.LogInformation("[Turso] Datenbank initialisiert");
        }
        catch (Exception ex)
        {
            _tursoAvailable = false;
            _logger.LogWarning(ex, "[Turso] Nicht verfügbar – verwende Memory Cache als Fallback");
        }
    }

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> generatorAsync, DateTime expiredTime = default)
    {
        var cached = await GetFromCache<T>(key);
        if (cached != null)
        {
            _logger.LogInformation("[Cache] Treffer: {Key}", key);
            return cached;
        }

        if (generatorAsync == null) return default;

        _logger.LogInformation("[Cache] Miss, lade Daten: {Key}", key);
        var result = await generatorAsync();

        if (result != null)
        {
            var expiry = expiredTime == default
                ? DateTime.Now.AddDays(_cacheSettings.ExpiryTime)
                : expiredTime;
            await SaveToCache(key, result, expiry);
        }

        return result;
    }

    public bool Any(string key)
    {
        if (_tursoAvailable)
        {
            try
            {
                var result = ExecuteScalar(
                    "SELECT COUNT(*) FROM Cache WHERE Key = ? AND ExpiresAt > ?",
                    key, DateTime.Now.ToString("o")).GetAwaiter().GetResult();
                return Convert.ToInt64(result) > 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Turso] Fehler bei Any – Fallback auf Memory");
                _tursoAvailable = false;
            }
        }

        return _memoryCache.TryGetValue(key, out var entry) && entry.ExpiresAt > DateTime.Now;
    }

    public void Remove(string key)
    {
        _memoryCache.TryRemove(key, out _);

        if (!_tursoAvailable) return;
        try
        {
            ExecuteSql("DELETE FROM Cache WHERE Key = ?", key).GetAwaiter().GetResult();
            _logger.LogInformation("[Cache] Gelöscht: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Turso] Fehler bei Remove");
            _tursoAvailable = false;
        }
    }

    public void Clear()
    {
        _memoryCache.Clear();

        if (!_tursoAvailable) return;
        try
        {
            ExecuteSql("DELETE FROM Cache").GetAwaiter().GetResult();
            _logger.LogInformation("[Cache] Komplett geleert");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Turso] Fehler bei Clear");
            _tursoAvailable = false;
        }
    }

    public void StartsWithClear(string prefix)
    {
        foreach (var key in _memoryCache.Keys.Where(k => k.StartsWith(prefix)))
            _memoryCache.TryRemove(key, out _);

        if (!_tursoAvailable) return;
        try
        {
            ExecuteSql("DELETE FROM Cache WHERE Key LIKE ?", prefix + "%").GetAwaiter().GetResult();
            _logger.LogInformation("[Cache] Gelöscht mit Prefix: {Prefix}", prefix);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Turso] Fehler bei StartsWithClear");
            _tursoAvailable = false;
        }
    }

    public List<string> GetAllKeys()
    {
        if (_tursoAvailable)
        {
            try
            {
                return ExecuteQuery("SELECT Key FROM Cache WHERE ExpiresAt > ?", DateTime.Now.ToString("o"))
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Turso] Fehler bei GetAllKeys – Fallback auf Memory");
                _tursoAvailable = false;
            }
        }

        return _memoryCache
            .Where(x => x.Value.ExpiresAt > DateTime.Now)
            .Select(x => x.Key)
            .ToList();
    }

    public T Get<T>(string key) => GetFromCache<T>(key).GetAwaiter().GetResult();

    public void Add(string key, object value)
    {
        if (value == null || Any(key)) return;
        SaveToCache(key, value, DateTime.Now.AddMinutes(_cacheSettings.ExpiryTime)).GetAwaiter().GetResult();
    }

    // ────────────────────────────────────────
    // Hilfsmethoden
    // ────────────────────────────────────────

    private async Task<T> GetFromCache<T>(string key)
    {
        if (_tursoAvailable)
        {
            try
            {
                var value = await ExecuteScalar(
                    "SELECT Value FROM Cache WHERE Key = ? AND ExpiresAt > ?",
                    key, DateTime.Now.ToString("o")) as string;

                if (value != null)
                    return JsonSerializer.Deserialize<T>(value)!;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Turso] Fehler beim Lesen – Fallback auf Memory");
                _tursoAvailable = false;
            }
        }

        if (_memoryCache.TryGetValue(key, out var entry) && entry.ExpiresAt > DateTime.Now)
        {
            _logger.LogInformation("[Memory] Cache-Treffer: {Key}", key);
            return JsonSerializer.Deserialize<T>(entry.Value)!;
        }

        return default;
    }

    private async Task SaveToCache(string key, object value, DateTime expiry)
    {
        var json = JsonSerializer.Serialize(value);
        _memoryCache[key] = (json, expiry);

        if (!_tursoAvailable) return;
        try
        {
            await ExecuteSql(
                "INSERT OR REPLACE INTO Cache (Key, Value, ExpiresAt) VALUES (?, ?, ?)",
                key, json, expiry.ToString("o"));
            _logger.LogInformation("[Turso] Cache gespeichert: {Key}, läuft ab: {Expiry:yyyy-MM-dd HH:mm}", key, expiry);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Turso] Fehler beim Speichern – nur in Memory gespeichert");
            _tursoAvailable = false;
        }
    }

    // ────────────────────────────────────────
    // Turso HTTP API
    // ────────────────────────────────────────

    private async Task ExecuteSql(string sql, params object[] args)
    {
        var body = BuildRequest(sql, args);
        var response = await _httpClient.PostAsync($"{_baseUrl}/v2/pipeline", body);
        response.EnsureSuccessStatusCode();
    }

    private async Task<object?> ExecuteScalar(string sql, params object[] args)
    {
        var body = BuildRequest(sql, args);
        var response = await _httpClient.PostAsync($"{_baseUrl}/v2/pipeline", body);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        try
        {
            var value = doc.RootElement
                .GetProperty("results")[0]
                .GetProperty("response")
                .GetProperty("result")
                .GetProperty("rows")[0][0]
                .GetProperty("value")
                .GetString();
            return value;
        }
        catch
        {
            return null;
        }
    }

    private async Task<List<string>> ExecuteQuery(string sql, params object[] args)
    {
        var body = BuildRequest(sql, args);
        var response = await _httpClient.PostAsync($"{_baseUrl}/v2/pipeline", body);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var keys = new List<string>();

        try
        {
            var rows = doc.RootElement
                .GetProperty("results")[0]
                .GetProperty("response")
                .GetProperty("result")
                .GetProperty("rows");

            foreach (var row in rows.EnumerateArray())
                keys.Add(row[0].GetProperty("value").GetString()!);
        }
        catch { }

        return keys;
    }

    private StringContent BuildRequest(string sql, params object[] args)
    {
        var arguments = args.Select(a => new { type = "text", value = a?.ToString() ?? "" }).ToArray();
        var payload = new
        {
            requests = new[]
            {
                new
                {
                    type = "execute",
                    stmt = new { sql, args = arguments }
                },
                new { type = "close" }
            }
        };
        return new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
    }
}
