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
        var adminName = headers.FirstOrDefault(x => x.Key.Equals("AdminName", StringComparison.OrdinalIgnoreCase)).Value.ToString();
        var adminCode = headers.FirstOrDefault(x => x.Key.Equals("AdminCode", StringComparison.OrdinalIgnoreCase)).Value.ToString();
        return adminName == _myApiClientSettings.AdminName && adminCode == _myApiClientSettings.AdminCode;
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
