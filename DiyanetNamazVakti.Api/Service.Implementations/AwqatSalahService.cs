using DiyanetNamazVakti.Api.Core.ValueObjects;
using PrayerTime.Service.Models;

namespace DiyanetNamazVakti.Api.Service.Implementations;

public class AwqatSalahService : IAwqatSalahService
{
    private readonly IAwqatSalahSettings _namazVaktiSettings;
    private readonly ICacheService _cacheService;
    private readonly IAwqatSalahConnectService _awqatSalahApiService;
    private readonly ILogger<AwqatSalahService> _logger;

    private const string YearlyCachePrefix = "smartcache_yearly_";
    private const string MonthlyCachePrefix = "smartcache_monthly_";
    private const string WeeklyCachePrefix = "smartcache_weekly_";
    private const string StalePrefix = "smartcache_stale_";

    public AwqatSalahService(
        IAwqatSalahSettings namazVaktiSettings,
        ICacheService cacheService,
        IAwqatSalahConnectService awqatSalahApiService,
        ILogger<AwqatSalahService> logger)
    {
        _namazVaktiSettings = namazVaktiSettings;
        _cacheService = cacheService;
        _awqatSalahApiService = awqatSalahApiService;
        _logger = logger;
    }

    // ────────────────────────────────────────
    // Admin: Jahres-Cache manuell aufbauen
    // ────────────────────────────────────────

    public async Task<bool> WarmCacheAsync(int cityId)
    {
        var yearlyKey = $"{YearlyCachePrefix}{cityId}_{DateTime.Now.Year}";
        var staleKey = $"{StalePrefix}{cityId}";
        var yearEnd = new DateTime(DateTime.Now.Year, 12, 31).ResetTimeToEndOfDay();

        try
        {
            _logger.LogInformation("[WarmCache] Starte DateRange für Stadt {CityId}", cityId);

            var yearly = await _awqatSalahApiService.CallService<List<AwqatSalahModel>>(
                "/api/PrayerTime/DateRange",
                MethodOption.Post,
                new DateRangeFilter
                {
                    CityId = cityId,
                    StartDate = new DateTime(DateTime.Now.Year, 1, 1),
                    EndDate = new DateTime(DateTime.Now.Year, 12, 31)
                },
                new CancellationToken());

            if (yearly != null && yearly.Count > 0)
            {
                _cacheService.Remove(yearlyKey);
                await _cacheService.GetOrCreateAsync(yearlyKey, () => Task.FromResult(yearly), yearEnd);
                await _cacheService.GetOrCreateAsync(staleKey, () => Task.FromResult(yearly), yearEnd.AddMonths(2));
                _logger.LogInformation("[WarmCache] Erfolgreich für Stadt {CityId}, {Count} Einträge", cityId, yearly.Count);
                return true;
            }

            _logger.LogWarning("[WarmCache] Keine Daten von Diyanet für Stadt {CityId}", cityId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WarmCache] Fehler für Stadt {CityId}", cityId);
            return false;
        }
    }

    // ────────────────────────────────────────
    // Kern: Cache-Kette (KEIN automatischer DateRange!)
    // ────────────────────────────────────────

    private async Task<List<AwqatSalahModel>> GetOrFetchData(int cityId, bool refresh = false)
    {
        var yearlyKey  = $"{YearlyCachePrefix}{cityId}_{DateTime.Now.Year}";
        var monthlyKey = $"{MonthlyCachePrefix}{cityId}_{DateTime.Now.Year}_{DateTime.Now.Month}";
        var weeklyKey  = $"{WeeklyCachePrefix}{cityId}_{DateTime.Now.Year}_{DateTime.Now.Month}_{DateTime.Now.Day}";
        var staleKey   = $"{StalePrefix}{cityId}";

        if (refresh)
        {
            _cacheService.Remove(yearlyKey);
            _cacheService.Remove(monthlyKey);
            _cacheService.Remove(weeklyKey);
        }

        var yearEnd  = new DateTime(DateTime.Now.Year, 12, 31).ResetTimeToEndOfDay();
        var monthEnd = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1).AddMonths(1).AddSeconds(-1);
        var weekEnd  = DateTime.Now.Date.AddDays(7);

        // ── Schritt 1: Jahres-Cache prüfen ──
        if (_cacheService.Any(yearlyKey))
        {
            _logger.LogInformation("[Cache] Jahres-Cache Treffer für Stadt {CityId}", cityId);
            return await _cacheService.GetOrCreateAsync<List<AwqatSalahModel>>(yearlyKey, null!, yearEnd);
        }

        // ── Schritt 2: Monats-Cache prüfen ──
        if (_cacheService.Any(monthlyKey))
        {
            _logger.LogInformation("[Cache] Monats-Cache Treffer für Stadt {CityId}", cityId);
            return await _cacheService.GetOrCreateAsync<List<AwqatSalahModel>>(monthlyKey, null!, monthEnd);
        }

        // ── Schritt 3: Monthly bei Diyanet laden (5/Tag, sicher) ──
        try
        {
            _logger.LogInformation("[Cache] Lade Monthly von Diyanet für Stadt {CityId}", cityId);
            var monthly = await _awqatSalahApiService.CallService<List<AwqatSalahModel>>(
                "/api/PrayerTime/Monthly/" + cityId,
                MethodOption.Get, null, new CancellationToken());

            if (monthly != null && monthly.Count > 0)
            {
                _logger.LogInformation("[Cache] Monthly erfolgreich für Stadt {CityId}", cityId);
                await _cacheService.GetOrCreateAsync(monthlyKey, () => Task.FromResult(monthly), monthEnd);
                await _cacheService.GetOrCreateAsync(staleKey, () => Task.FromResult(monthly), monthEnd.AddMonths(2));
                return monthly;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Cache] Monthly fehlgeschlagen für Stadt {CityId}", cityId);
        }

        // ── Schritt 4: Wochen-Cache prüfen ──
        if (_cacheService.Any(weeklyKey))
        {
            _logger.LogInformation("[Cache] Wochen-Cache Treffer für Stadt {CityId}", cityId);
            return await _cacheService.GetOrCreateAsync<List<AwqatSalahModel>>(weeklyKey, null!, weekEnd);
        }

        // ── Schritt 5: Weekly bei Diyanet laden ──
        try
        {
            _logger.LogInformation("[Cache] Lade Weekly von Diyanet für Stadt {CityId}", cityId);
            var weekly = await _awqatSalahApiService.CallService<List<AwqatSalahModel>>(
                "/api/PrayerTime/Weekly/" + cityId,
                MethodOption.Get, null, new CancellationToken());

            if (weekly != null && weekly.Count > 0)
            {
                _logger.LogInformation("[Cache] Weekly erfolgreich für Stadt {CityId}", cityId);
                await _cacheService.GetOrCreateAsync(weeklyKey, () => Task.FromResult(weekly), weekEnd);
                await _cacheService.GetOrCreateAsync(staleKey, () => Task.FromResult(weekly), DateTime.Now.AddDays(14));
                return weekly;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Cache] Weekly fehlgeschlagen für Stadt {CityId}", cityId);
        }

        // ── Schritt 6: Stale Cache zurückgeben ──
        if (_cacheService.Any(staleKey))
        {
            _logger.LogWarning("[Cache] Alle Diyanet-Anfragen fehlgeschlagen. Veraltete Daten für Stadt {CityId}", cityId);
            return await _cacheService.GetOrCreateAsync<List<AwqatSalahModel>>(staleKey, null!, DateTime.Now.AddMonths(2));
        }

        // ── Schritt 7: Kein Fallback möglich ──
        _logger.LogError("[Cache] Kein Cache und Diyanet nicht erreichbar für Stadt {CityId}", cityId);
        return null;
    }

    // ────────────────────────────────────────
    // Daily → heute
    // ────────────────────────────────────────

    public async Task<List<AwqatSalahModel>> DailyAwqatSalah(int cityId, bool refresh = false)
    {
        var data = await GetOrFetchData(cityId, refresh);
        if (data is null) return null;

        var today = DateTime.Now.ToString("dd.MM.yyyy");
        return data
            .Where(x => x.GregorianDateShort == today)
            .ToList();
    }

    // ────────────────────────────────────────
    // Weekly → heute + 7 Tage
    // ────────────────────────────────────────

    public async Task<List<AwqatSalahModel>> WeeklyAwqatSalah(int cityId, bool refresh = false)
    {
        var data = await GetOrFetchData(cityId, refresh);
        if (data is null) return null;

        var today = DateTime.Now.Date;
        var endOfWeek = today.AddDays(7);

        return data
            .Where(x =>
            {
                if (DateTime.TryParseExact(x.GregorianDateShort, "dd.MM.yyyy",
                    null, System.Globalization.DateTimeStyles.None, out var date))
                    return date >= today && date <= endOfWeek;
                return false;
            })
            .ToList();
    }

    // ────────────────────────────────────────
    // Monthly → heute + 30 Tage
    // ────────────────────────────────────────

    public async Task<List<AwqatSalahModel>> MonthlyAwqatSalah(int cityId, bool refresh = false)
    {
        var data = await GetOrFetchData(cityId, refresh);
        if (data is null) return null;

        var today = DateTime.Now.Date;
        var endOfMonth = today.AddDays(30);

        return data
            .Where(x =>
            {
                if (DateTime.TryParseExact(x.GregorianDateShort, "dd.MM.yyyy",
                    null, System.Globalization.DateTimeStyles.None, out var date))
                    return date >= today && date <= endOfMonth;
                return false;
            })
            .ToList();
    }

    // ────────────────────────────────────────
    // Yearly → aus Cache (kein automatischer DateRange!)
    // ────────────────────────────────────────

    public async Task<List<AwqatSalahModel>> YearlyAwqatSalah(DateRangeFilter dateRange, bool refresh = false)
    {
        if (dateRange.StartDate > dateRange.EndDate)
            throw new InvalidTransactionException(Dictionary.StartDateNotAvailable);

        return await GetOrFetchData(dateRange.CityId, refresh);
    }

    // ────────────────────────────────────────
    // Ramadan & Eid → eigener Cache
    // ────────────────────────────────────────

    public async Task<List<AwqatSalahModel>> RamadanAwqatSalah(int cityId)
    {
        var result = await _cacheService.GetOrCreateAsync(
            MethodBase.GetCurrentMethod()!.DeclaringType!.FullName! + cityId,
            async () => await _awqatSalahApiService.CallService<List<AwqatSalahModel>>(
                "/api/PrayerTime/Ramadan/" + cityId,
                MethodOption.Get, null, new CancellationToken()));

        if (result is null)
            _cacheService.Remove(MethodBase.GetCurrentMethod()!.DeclaringType!.FullName! + cityId);

        return result;
    }

    public async Task<EidAwqatSalahModel> EidAwqatSalah(int cityId)
    {
        var result = await _cacheService.GetOrCreateAsync(
            MethodBase.GetCurrentMethod()!.DeclaringType!.FullName! + cityId,
            async () => await _awqatSalahApiService.CallService<EidAwqatSalahModel>(
                "/api/PrayerTime/Eid/" + cityId,
                MethodOption.Get, null, new CancellationToken()),
            new DateTime(DateTime.Now.Year, 12, 31).ResetTimeToEndOfDay());

        if (result is null)
            _cacheService.Remove(MethodBase.GetCurrentMethod()!.DeclaringType!.FullName! + cityId);

        return result;
    }
}
