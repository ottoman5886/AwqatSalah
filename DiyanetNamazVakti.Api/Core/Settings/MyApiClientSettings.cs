using DiyanetNamazVakti.Api.Core.Settings;

namespace DiyanetNamazVakti.Api.Core.Settings;

public class MyApiClientSettings : IMyApiClientSettings
{
    public List<ApiKey> ApiKeys { get; set; } = new();
}
