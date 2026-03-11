using DiyanetNamazVakti.Api.Core.Settings;

namespace DiyanetNamazVakti.Api.Core;

public interface IMyApiClientSettings
{
    List<ApiKey> ApiKeys { get; set; }
}
