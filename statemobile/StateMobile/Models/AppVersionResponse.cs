namespace StateMobile.Models
{
    public class AppVersionResponse
    {
        public string LatestVersion { get; set; } = string.Empty;
        public bool ForceUpdate { get; set; }
        public string UpdateUrl { get; set; } = string.Empty;
    }
}
