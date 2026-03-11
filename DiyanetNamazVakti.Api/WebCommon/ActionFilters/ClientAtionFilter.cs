using Microsoft.AspNetCore.Mvc.Filters;
using DiyanetNamazVakti.Api.Core.Settings;

namespace DiyanetNamazVakti.Api.WebCommon.ActionFilters;

public class ClientAtionFilter : IAsyncActionFilter
{
    private readonly IMyApiClientSettings _myApiClientSettings;

    public ClientAtionFilter(IMyApiClientSettings myApiClientSettings)
    {
        _myApiClientSettings = myApiClientSettings;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var headers = context.HttpContext.Request.Headers;

        var apiKeyHeader = headers.FirstOrDefault(x =>
            x.Key.Equals("X-API-Key", StringComparison.OrdinalIgnoreCase));

        var isAuthorized = !string.IsNullOrEmpty(apiKeyHeader.Key) &&
            _myApiClientSettings.ApiKeys.Any(k =>
                k.Key.Equals(apiKeyHeader.Value.ToString(), StringComparison.Ordinal));

        if (isAuthorized)
            await next();
        else
            throw new BadHttpRequestException(string.Format(Dictionary.AccessDenied));
    }
}
