using Microsoft.AspNetCore.Mvc;

namespace DiyanetNamazVakti.Api.Controllers;

[Route("[controller]")]
[ApiController]
public class HealthController : ControllerBase
{
    private readonly ICacheService _cacheService;
    private readonly IMyApiClientSettings _myApiClientSettings;

    public HealthController(ICacheService cacheService, IMyApiClientSettings myApiClientSettings)
    {
        _cacheService = cacheService;
        _myApiClientSettings = myApiClientSettings;
    }

    [HttpGet]
    [HttpPost]
    public ActionResult Get()
    {
        var isAdmin = false;
        var apiKey = HttpContext.Request.Headers
            .FirstOrDefault(x => x.Key.Equals("X-API-Key", StringComparison.OrdinalIgnoreCase))
            .Value.ToString();

        if (!string.IsNullOrEmpty(apiKey))
            isAdmin = _myApiClientSettings.ApiKeys.Any(k =>
                k.Key.Equals(apiKey, StringComparison.Ordinal) && k.IsAdmin);

        var keys = isAdmin ? _cacheService.GetAllKeys() : new List<string>();
        var cachedCities = keys.Count(k => k.StartsWith("smartcache_yearly_"));

        return Ok(new
        {
            status = "ok",
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            turso = "reachable",
            cachedCities,
            cachedKeys = isAdmin ? keys : null
        });
    }
}
