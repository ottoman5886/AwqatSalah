namespace DiyanetNamazVakti.Api.Core.Settings;
public class MyApiClientSettings : IMyApiClientSettings
{
    public string UserName { get; set; }
    public string SecretCode { get; set; }
    public string AdminName { get; set; }
    public string AdminCode { get; set; }
}
