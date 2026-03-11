using Microsoft.AspNetCore.Mvc;
using PrayerTime.Service.Models;

namespace DiyanetNamazVakti.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AwqatSalahController : ControllerBase
{
    private readonly IAwqatSalahService _awqatSalahService;
    private readonly IMyApiClientSettings _myApiClientSettings;

    public AwqatSalahController(IAwqatSalahService awqatSalahService, IMyApiClientSettings myApiClientSettings)
    {
        _awqatSalahService = awqatSalahService;
        _myApiClientSettings = myApiClientSettings;
    }

    private bool IsAdminRequest()
    {
        var headers = HttpContext.Request.Headers;
        var apiKey = headers.FirstOrDefault(x =>
            x.Key.Equals("X-API-Key", StringComparison.OrdinalIgnoreCase)).Value.ToString();

        return _myApiClientSettings.ApiKeys.Any(k =>
            k.Key.Equals(apiKey, StringComparison.Ordinal) && k.IsAdmin);
    }

    [HttpPost("WarmCache/{cityId}")]
    public async Task<ActionResult> WarmCache(int cityId)
    {
        if (!IsAdminRequest())
            return StatusCode(403, new { success = false, message = "Nur für Admins erlaubt." });

        var success = await _awqatSalahService.WarmCacheAsync(cityId);

        if (success)
            return Ok(new { success = true, message = $"Jahres-Cache für Stadt {cityId} erfolgreich aufgebaut." });
        else
            return StatusCode(500, new { success = false, message = $"Fehler beim Aufbau des Caches für Stadt {cityId}." });
    }

    [HttpGet("Daily/{cityId}")]
    public async Task<ActionResult<IResult>> AwqatSalahDaily(int cityId, [FromQuery] bool refresh = false)
    {
        if (refresh && !IsAdminRequest())
            return StatusCode(403, new { success = false, message = "Refresh nur für Admins erlaubt." });
        return new SuccessDataResult<List<AwqatSalahModel>>(await _awqatSalahService.DailyAwqatSalah(cityId, refresh));
    }

    [HttpGet("Weekly/{cityId}")]
    public async Task<ActionResult<IResult>> AwqatSalahWeekly(int cityId, [FromQuery] bool refresh = false)
    {
        if (refresh && !IsAdminRequest())
            return StatusCode(403, new { success = false, message = "Refresh nur für Admins erlaubt." });
        return new SuccessDataResult<List<AwqatSalahModel>>(await _awqatSalahService.WeeklyAwqatSalah(cityId, refresh));
    }

    [HttpGet("Monthly/{cityId}")]
    public async Task<ActionResult<IResult>> AwqatSalahMonthly(int cityId, [FromQuery] bool refresh = false)
    {
        if (refresh && !IsAdminRequest())
            return StatusCode(403, new { success = false, message = "Refresh nur für Admins erlaubt." });
        return new SuccessDataResult<List<AwqatSalahModel>>(await _awqatSalahService.MonthlyAwqatSalah(cityId, refresh));
    }

    [HttpPost("Yearly")]
    public async Task<ActionResult<IResult>> AwqatSalahDateRange([FromBody] DateRangeFilter filter, [FromQuery] bool refresh = false)
    {
        if (refresh && !IsAdminRequest())
            return StatusCode(403, new { success = false, message = "Refresh nur für Admins erlaubt." });
        return new SuccessDataResult<List<AwqatSalahModel>>(await _awqatSalahService.YearlyAwqatSalah(filter, refresh));
    }

    [HttpGet("Eid/{cityId}")]
    public async Task<ActionResult<IResult>> AwqatSalahEid(int cityId)
    {
        return new SuccessDataResult<EidAwqatSalahModel>(await _awqatSalahService.EidAwqatSalah(cityId));
    }

    [HttpGet("Ramadan/{cityId}")]
    public async Task<ActionResult<IResult>> AwqatSalahRamadan(int cityId)
    {
        return new SuccessDataResult<List<AwqatSalahModel>>(await _awqatSalahService.RamadanAwqatSalah(cityId));
    }
}
