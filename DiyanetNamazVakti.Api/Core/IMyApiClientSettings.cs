namespace DiyanetNamazVakti.Api.Core
{
    public interface IMyApiClientSettings
    {
        public string UserName { get; set; }
        public string SecretCode { get; set; }
        public string AdminName { get; set; }
        public string AdminCode { get; set; }
    }
}
