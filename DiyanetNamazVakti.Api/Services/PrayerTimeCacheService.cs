using Microsoft.Extensions.Caching.Memory;

namespace DiyanetNamazVakti.Api.Services;

public class PrayerTimeCacheService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<PrayerTimeCacheService> _logger;

    public PrayerTimeCacheService(IMemoryCache cache, ILogger<PrayerTimeCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public bool TryGetMonthly(int cityId, out object? data)
    {
        return _cache.TryGetValue(MonthlyKey(cityId), out data);
    }

    public void SetMonthly(int cityId, object data)
    {
        var now = DateTime.Now;
        var expiry = new DateTime(now.Year, now.Month, 1)
            .AddMonths(1)
            .AddHours(2);

        _cache.Set(MonthlyKey(cityId), data, new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = expiry
        });

        _logger.LogInformation(
            "[Cache] Gespeichert. Stadt={CityId}, Ablauf={Expiry:yyyy-MM-dd HH:mm}",
            cityId, expiry);
    }

    public bool TryGetStatic(string key, out object? data)
    {
        return _cache.TryGetValue($"static_{key}", out data);
    }

    public void SetStatic(string key, object data)
    {
        _cache.Set($"static_{key}", data, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(30)
        });

        _logger.LogInformation("[Cache] Statische Daten gespeichert. Key={Key}", key);
    }

    private static string MonthlyKey(int cityId)
    {
        var now = DateTime.Now;
        return $"prayertime_{cityId}_{now.Year}_{now.Month}";
    }
}
