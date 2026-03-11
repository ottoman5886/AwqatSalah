using DiyanetNamazVakti.Api.Core.ValueObjects;
using PrayerTime.Service.Models;

namespace DiyanetNamazVakti.Api.Service.Implementations;

public class AwqatSalahService : IAwqatSalahService
{
    private readonly IAwqatSalahSettings _namazVaktiSettings;
    private readonly ICacheService _cacheService;
    private readonly IAwqatSalahConnectService _awqatSalahApiService;

    // Cache Key Prefix für Jahres-Daten
    private const string YearlyCachePrefix = "smartcache_yearly_";

    public AwqatSalahService(IAwqatSalahSettings namazVaktiSettings, ICacheService cacheService, IAwqatSalahConnectService awqatSalahApiService)
    {
        _namazVaktiSettings = namazVaktiSettings;
        _cacheService = cacheService;
        _awqatSalahApiService = awqatSalahApiService;
    }

    // ────────────────────────────────────────
    // Kern: Jahres-Cache als Basis für alles
    // ────────────────────────────────────────

    private async Task<List<AwqatSalahModel>> GetOrFetchYearly(int cityId, bool refresh = false)
    {
        var cacheKey = $"{YearlyCachePrefix}{cityId}_{DateTime.Now.Year}";

        // Bypass: Cache löschen damit frisch von Diyanet geladen wird
        if (refresh)
            _cacheService.Remove(cacheKey);

        var yearEnd = new DateTime(DateTime.Now.Year, 12, 31).ResetTimeToEndOfDay();

        var result = await _cacheService.GetOrCreateAsync(cacheKey,
            async () => await _awqatSalahApiService.CallService<List<AwqatSalahModel>>(
                "/api/PrayerTime/DateRange",
                MethodOption.Post,
                new DateRangeFilter
                {
                    CityId = cityId,
                    StartDate = new DateTime(DateTime.Now.Year, 1, 1),
                    EndDate = new DateTime(DateTime.Now.Year, 12, 31)
                },
                new CancellationToken()),
            yearEnd);

        if (result is null)
            _cacheService.Remove(cacheKey);

        return result;
    }

    // ────────────────────────────────────────
    // Daily → aus Jahres-Cache filtern
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
    // Weekly → aus Jahres-Cache filtern
    // ────────────────────────────────────────

    public async Task<List<AwqatSalahModel>> WeeklyAwqatSalah(int cityId, bool refresh = false)
    {
        var yearly = await GetOrFetchYearly(cityId, refresh);
        if (yearly is null) return null;

        var startOfWeek = DateTime.Now.Date.AddDays(-(int)DateTime.Now.DayOfWeek + 1);
        var endOfWeek = startOfWeek.AddDays(6);

        return yearly
            .Where(x =>
            {
                if (DateTime.TryParseExact(x.GregorianDateShort, "dd.MM.yyyy",
                    null, System.Globalization.DateTimeStyles.None, out var date))
                    return date >= startOfWeek && date <= endOfWeek;
                return false;
            })
            .ToList();
    }

    // ────────────────────────────────────────
    // Monthly → aus Jahres-Cache filtern
    // ────────────────────────────────────────

    public async Task<List<AwqatSalahModel>> MonthlyAwqatSalah(int cityId, bool refresh = false)
    {
        var yearly = await GetOrFetchYearly(cityId, refresh);
        if (yearly is null) return null;

        var currentMonth = DateTime.Now.Month;

        return yearly
            .Where(x =>
            {
                if (DateTime.TryParseExact(x.GregorianDateShort, "dd.MM.yyyy",
                    null, System.Globalization.DateTimeStyles.None, out var date))
                    return date.Month == currentMonth;
                return false;
            })
            .ToList();
    }

    // ────────────────────────────────────────
    // Yearly → direkt (Basis für alles)
    // ────────────────────────────────────────

    public async Task<List<AwqatSalahModel>> YearlyAwqatSalah(DateRangeFilter dateRange, bool refresh = false)
    {
        if (dateRange.StartDate > dateRange.EndDate)
            throw new InvalidTransactionException(Dictionary.StartDateNotAvailable);

        return await GetOrFetchYearly(dateRange.CityId, refresh);
    }

    // ────────────────────────────────────────
    // Ramadan & Eid → eigener Cache (jährlich)
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
