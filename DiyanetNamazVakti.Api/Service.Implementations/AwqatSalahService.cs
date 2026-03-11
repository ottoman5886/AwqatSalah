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
    // Kern: Jahres-Cache mit Fallback-Kette
    // ────────────────────────────────────────

    private async Task<List<AwqatSalahModel>> GetOrFetchYearly(int cityId, bool refresh = false)
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
        if (_cacheService.Any(yearlyKey) && !refresh)
        {
            _logger.LogInformation("[Fallback] Jahres-Cache Treffer für Stadt {CityId}", cityId);
            return await _cacheService.GetOrCreateAsync<List<AwqatSalahModel>>(yearlyKey, null!, yearEnd);
        }

        // ── Schritt 2: DateRange bei Diyanet versuchen ──
        try
        {
            _logger.LogInformation("[Fallback] Versuche DateRange für Stadt {CityId}", cityId);
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
                _logger.LogInformation("[Fallback] DateRange erfolgreich für Stadt {CityId}", cityId);
                await _cacheService.GetOrCreateAsync(yearlyKey, () => Task.FromResult(yearly), yearEnd);
                await _cacheService.GetOrCreateAsync(staleKey, () => Task.FromResult(yearly), yearEnd.AddMonths(2));
                return yearly;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Fallback] DateRange fehlgeschlagen für Stadt {CityId}", cityId);
        }

        // ── Schritt 3: Monats-Cache prüfen ──
        if (_cacheService.Any(monthlyKey))
        {
            _logger.LogInformation("[Fallback] Monats-Cache Treffer für Stadt {CityId}", cityId);
            return await _cacheService.GetOrCreateAsync<List<AwqatSalahModel>>(monthlyKey, null!, monthEnd);
        }

        // ── Schritt 4: Monthly bei Diyanet versuchen ──
        try
        {
            _logger.LogInformation("[Fallback] Versuche Monthly für Stadt {CityId}", cityId);
            var monthly = await _awqatSalahApiService.CallService<List<AwqatSalahModel>>(
                "/api/PrayerTime/Monthly/" + cityId,
                MethodOption.Get, null, new CancellationToken());

            if (monthly != null && monthly.Count > 0)
            {
                _logger.LogInformation("[Fallback] Monthly erfolgreich für Stadt {CityId}", cityId);
                await _cacheService.GetOrCreateAsync(monthlyKey, () => Task.FromResult(monthly), monthEnd);
                await _cacheService.GetOrCreateAsync(staleKey, () => Task.FromResult(monthly), monthEnd.AddMonths(2));
                return monthly;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Fallback] Monthly fehlgeschlagen für Stadt {CityId}", cityId);
        }

        // ── Schritt 5: Wochen-Cache prüfen ──
        if (_cacheService.Any(weeklyKey))
        {
            _logger.LogInformation("[Fallback] Wochen-Cache Treffer für Stadt {CityId}", cityId);
            return await _cacheService.GetOrCreateAsync<List<AwqatSalahModel>>(weeklyKey, null!, weekEnd);
        }

        // ── Schritt 6: Weekly bei Diyanet versuchen ──
        try
        {
            _logger.LogInformation("[Fallback] Versuche Weekly für Stadt {CityId}", cityId);
            var weekly = await _awqatSalahApiService.CallService<List<AwqatSalahModel>>(
                "/api/PrayerTime/Weekly/" + cityId,
                MethodOption.Get, null, new CancellationToken());

            if (weekly != null && weekly.Count > 0)
            {
                _logger.LogInformation("[Fallback] Weekly erfolgreich für Stadt {CityId}", cityId);
                await _cacheService.GetOrCreateAsync(weeklyKey, () => Task.FromResult(weekly), weekEnd);
                await _cacheService.GetOrCreateAsync(staleKey, () => Task.FromResult(weekly), DateTime.Now.AddDays(14));
                return weekly;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Fallback] Weekly fehlgeschlagen für Stadt {CityId}", cityId);
        }

        // ── Schritt 7: Letzten bekannten Cache zurückgeben (Stale) ──
        if (_cacheService.Any(staleKey))
        {
            _logger.LogWarning("[Fallback] Alle Diyanet-Anfragen fehlgeschlagen. Gebe veraltete Daten zurück für Stadt {CityId}", cityId);
            return await _cacheService.GetOrCreateAsync<List<AwqatSalahModel>>(staleKey, null!, DateTime.Now.AddMonths(2));
        }

        // ── Schritt 8: Komplett kein Fallback möglich ──
        _logger.LogError("[Fallback] Kein Cache und Diyanet nicht erreichbar für Stadt {CityId}", cityId);
        return null;
    }

    // ────────────────────────────────────────
    // Daily → heute
    // ────────────────────────────────────────

    public async Task<List<AwqatSalahModel>> DailyAwqatSalah(int cityId, bool refresh = false)
    {
        var yearly = await GetOrFetchYearly(cityId, refresh);
        if (yearly is null) return null;

        var today = DateTime.Now.ToString("dd.MM.yyyy");
        return yearly
            .Where(x => x.GregorianDateShort == today)
            .ToList();
    }

    // ────────────────────────────────────────
    // Weekly → heute + 7 Tage
    // ────────────────────────────────────────

    public async Task<List<AwqatSalahModel>> WeeklyAwqatSalah(int cityId, bool refresh = false)
    {
        var yearly = await GetOrFetchYearly(cityId, refresh);
        if (yearly is null) return null;

        var today = DateTime.Now.Date;
        var endOfWeek = today.AddDays(7);

        return yearly
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
        var yearly = await GetOrFetchYearly(cityId, refresh);
        if (yearly is null) return null;

        var today = DateTime.Now.Date;
        var endOfMonth = today.AddDays(30);

        return yearly
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
    // Yearly → direkt
    // ────────────────────────────────────────

    public async Task<List<AwqatSalahModel>> YearlyAwqatSalah(DateRangeFilter dateRange, bool refresh = false)
    {
        if (dateRange.StartDate > dateRange.EndDate)
            throw new InvalidTransactionException(Dictionary.StartDateNotAvailable);

        return await GetOrFetchYearly(dateRange.CityId, refresh);
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
