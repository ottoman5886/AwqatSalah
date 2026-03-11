using PrayerTime.Service.Models;

namespace DiyanetNamazVakti.Api.Service;

public interface IAwqatSalahService
{
    Task<bool> WarmCacheAsync(int cityId);
    Task<List<AwqatSalahModel>> DailyAwqatSalah(int cityId, bool refresh = false);
    Task<List<AwqatSalahModel>> WeeklyAwqatSalah(int cityId, bool refresh = false);
    Task<List<AwqatSalahModel>> MonthlyAwqatSalah(int cityId, bool refresh = false);
    Task<List<AwqatSalahModel>> YearlyAwqatSalah(DateRangeFilter dateRange, bool refresh = false);
    Task<EidAwqatSalahModel> EidAwqatSalah(int cityId);
    Task<List<AwqatSalahModel>> RamadanAwqatSalah(int cityId);
}
