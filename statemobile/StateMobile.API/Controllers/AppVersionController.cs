using Microsoft.AspNetCore.Mvc;
using StateMobile.API.Models;

namespace StateMobile.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AppVersionController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _env;

        public AppVersionController(IConfiguration configuration, IWebHostEnvironment env)
        {
            _configuration = configuration;
            _env = env;
        }

        [HttpGet]
        public IActionResult GetAppVersion()
        {
            var appVersion = new AppVersionModel
            {
                LatestVersion = _configuration["AppVersion:LatestVersion"] ?? "1.0",
                ForceUpdate = bool.TryParse(_configuration["AppVersion:ForceUpdate"], out bool force) && force,
                UpdateUrl = _configuration["AppVersion:UpdateUrl"] ?? ""
            };

            return Ok(appVersion);
        }

        [HttpGet("download/latest")]
        public IActionResult DownloadLatest()
        {
            var latestVersion = _configuration["AppVersion:LatestVersion"] ?? "1.0";
            var expectedApkName = $"StateMobile_v{latestVersion}.apk";
            
            Console.WriteLine($"📥 [AppVersion] Download requested. Target: {latestVersion}");
            var updatesFolder = Path.Combine(_env.ContentRootPath, "wwwroot", "updates");

            if (!System.IO.Directory.Exists(updatesFolder))
                return NotFound(new { error = "Folder not found", path = updatesFolder });

            var dirInfo = new DirectoryInfo(updatesFolder);
            var targetFile = dirInfo.GetFiles(expectedApkName).FirstOrDefault() 
                          ?? dirInfo.GetFiles("*.apk").OrderByDescending(f => f.LastWriteTime).FirstOrDefault();

            if (targetFile == null)
            {
                Console.WriteLine("❌ [AppVersion] No APK files found.");
                return NotFound("No APK files found.");
            }

            Console.WriteLine($"✅ [AppVersion] Serving: {targetFile.Name}");
            return PhysicalFile(targetFile.FullName, "application/vnd.android.package-archive", targetFile.Name);
        }
    }
}
