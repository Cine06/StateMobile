using Microsoft.AspNetCore.Mvc;
using StateMobile.API.Models;
using StateMobile.API.Services;

namespace StateMobile.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProjectController : ControllerBase
    {
        private readonly IDatabaseService _dbService;

        public ProjectController(IDatabaseService dbService)
        {
            _dbService = dbService;
        }

        [HttpGet("list")]
        public async Task<IActionResult> GetProjects()
        {
            try
            {
                var projects = await _dbService.GetProjectsAsync();
                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                if (_dbService is DatabaseService dbSvc)
                    await dbSvc.SetCoverPhotoUrlsAsync(projects, baseUrl);
                return Ok(projects);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving projects: {ex.Message}");
                return StatusCode(500, "An error occurred while retrieving projects.");
            }
        }

        [HttpGet("{controlNo}")]
        public async Task<IActionResult> GetProjectByControlNo(string controlNo)
        {
            try
            {
                var project = await _dbService.GetProjectByControlNoAsync(controlNo);
                if (project == null) return NotFound(new { error = "Project not found" });
                return Ok(project);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving project {controlNo}: {ex.Message}");
                return StatusCode(500, "An error occurred while retrieving project.");
            }
        }

        [HttpGet("status-list")]
        public async Task<IActionResult> GetStatusList()
        {
            try
            {
                var list = await _dbService.GetStatusListAsync();
                return Ok(list);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving status list: {ex.Message}");
                return StatusCode(500, "An error occurred while retrieving status list.");
            }
        }

        [HttpGet("model-list")]
        public async Task<IActionResult> GetModelList()
        {
            try
            {
                var list = await _dbService.GetHouseModelListAsync();
                return Ok(list);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving model list: {ex.Message}");
                return StatusCode(500, "An error occurred while retrieving model list.");
            }
        }

        [HttpGet("engineer-list")]
        public async Task<IActionResult> GetEngineerList()
        {
            try
            {
                var list = await _dbService.GetAssignedEngineersAsync();
                return Ok(list);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving engineer list: {ex.Message}");
                return StatusCode(500, "An error occurred while retrieving engineer list.");
            }
        }

        [HttpGet("filtered")]
        public async Task<IActionResult> GetFilteredProjects(
            [FromQuery] string? statusCodes, 
            [FromQuery] string? engineerCodes, 
            [FromQuery] string? modelCodes,
            [FromQuery] string? sortBy = "projects", 
            [FromQuery] string? sortDir = "ASC")
        {
            try
            {
                var projects = await _dbService.GetFilteredProjectsAsync(statusCodes, engineerCodes, modelCodes, sortBy, sortDir);
                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                if (_dbService is DatabaseService dbSvc)
                    await dbSvc.SetCoverPhotoUrlsAsync(projects, baseUrl);
                return Ok(projects);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving filtered projects: {ex.Message}");
                return StatusCode(500, "An error occurred while retrieving filtered projects.");
            }
        }

        // ─── Project Diary Endpoints ───

        [HttpGet("diary")]
        public async Task<IActionResult> GetProjectDiary(
            [FromQuery] string controlNo,
            [FromQuery] string? startDate = "",
            [FromQuery] string? endDate = "",
            [FromQuery] string? auditUser = "")
        {
            try
            {
                var entries = await _dbService.GetProjectDiaryAsync(controlNo, startDate ?? "", endDate ?? "", auditUser ?? "");
                return Ok(new { success = true, data = entries });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving diary: {ex.Message}");
                return Ok(new { success = false, message = ex.Message });
            }
        }

        [HttpPost("diary/save")]
        public async Task<IActionResult> SaveDiaryEntry([FromBody] SaveDiaryRequest request)
        {
            try
            {
                var entryId = await _dbService.SaveProjectDiaryAsync(request);
                return Ok(new { success = entryId > 0, id = entryId, message = entryId > 0 ? "Saved successfully!" : "Save failed." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving diary: {ex.Message}");
                return Ok(new { success = false, message = $"Save failed: {ex.Message}" });
            }
        }

        [HttpPost("diary/update")]
        public async Task<IActionResult> UpdateDiaryEntry([FromBody] SaveDiaryRequest request)
        {
            try
            {
                var entryId = await _dbService.UpdateProjectDiaryAsync(request);
                return Ok(new { success = entryId > 0, id = entryId, message = entryId > 0 ? "Updated successfully!" : "Update failed." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating diary: {ex.Message}");
                return Ok(new { success = false, message = $"Update failed: {ex.Message}" });
            }
        }

        [HttpDelete("diary/{diaryEntryId}")]
        public async Task<IActionResult> DeleteDiaryEntry(int diaryEntryId)
        {
            try
            {
                var result = await _dbService.DeleteProjectDiaryAsync(diaryEntryId);
                return Ok(new { success = result });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting diary: {ex.Message}");
                return Ok(new { success = false, message = ex.Message });
            }
        }

        [HttpGet("diary/files")]
        public async Task<IActionResult> GetProjectDiaryFiles(
            [FromQuery] string controlNo,
            [FromQuery] string? startDate = "",
            [FromQuery] string? endDate = "",
            [FromQuery] string? auditUser = "")
        {
            try
            {
                var files = await _dbService.GetProjectDiaryFilesAsync(controlNo, startDate ?? "", endDate ?? "", auditUser ?? "");
                return Ok(new { success = true, data = files });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving diary files: {ex.Message}");
                return Ok(new { success = false, message = ex.Message });
            }
        }

        [HttpPost("diary/files/save")]
        public async Task<IActionResult> SaveDiaryFile([FromBody] SaveDiaryFileRequest request)
        {
            try
            {
                var result = await _dbService.SaveProjectDiaryFileAsync(request);
                return Ok(new { success = result, message = result ? "File saved successfully!" : "File save failed." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving diary file: {ex.Message}");
                return Ok(new { success = false, message = ex.Message });
            }
        }

        [HttpDelete("diary/files/{fileId}")]
        public async Task<IActionResult> DeleteDiaryFile(int fileId)
        {
            try
            {
                var result = await _dbService.DeleteProjectDiaryFileAsync(fileId);
                return Ok(new { success = result });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting diary file: {ex.Message}");
                return Ok(new { success = false, message = ex.Message });
            }
        }

        [HttpGet("diary/files/content/{streamId}")]
        public async Task<IActionResult> GetDiaryFileContent(string streamId)
        {
            try
            {
                var (fileBytes, fileName, contentType) = await _dbService.GetDiaryFileContentAsync(streamId);
                if (fileBytes != null && fileBytes.Length > 0)
                {
                    return File(fileBytes, contentType, fileName);
                }
                return NotFound(new { success = false, message = "File not found in DMS." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting diary file content: {ex.Message}");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ─── Project Profile Picture ───

        [HttpGet("profile-pic/{controlNo}")]
        public async Task<IActionResult> GetProfilePic(string controlNo)
        {
            try
            {
                var (fileBytes, fileName, contentType) = await _dbService.GetProjectProfilePicAsync(controlNo);
                if (fileBytes != null && fileBytes.Length > 0)
                {
                    return File(fileBytes, contentType, fileName);
                }
                return NoContent(); // Web counterpart returns 204
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting profile pic: {ex.Message}");
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPost("profile-pic/upload")]
        public async Task<IActionResult> UploadProfilePic([FromForm] IFormFile photo, [FromForm] string controlNo, [FromForm] string? auditUser = "MobileUser")
        {
            try
            {
                if (photo == null || photo.Length == 0) return BadRequest("No photo provided.");

                using var ms = new MemoryStream();
                await photo.CopyToAsync(ms);
                var content = ms.ToArray();

                var result = await _dbService.SaveProjectProfilePicAsync(controlNo, content, photo.FileName, photo.ContentType, auditUser ?? "MobileUser");
                return Ok(new { success = result, message = result ? "Photo uploaded successfully!" : "Photo upload failed." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error uploading profile pic: {ex.Message}");
                return StatusCode(500, ex.Message);
            }
        }

        [HttpGet("engineers-pic/{controlNo}")]
        public async Task<IActionResult> GetEngineersPic(string controlNo, [FromQuery] string entityCodes)
        {
            try
            {
                var (fileBytes, fileName, contentType) = await _dbService.GetEngineersPicAsync(controlNo, entityCodes);
                if (fileBytes != null && fileBytes.Length > 0)
                {
                    return File(fileBytes, contentType, fileName);
                }
                return NoContent();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting engineers pic: {ex.Message}");
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPost("save-engineer")]
        public async Task<IActionResult> SaveEngineer([FromBody] SaveEngineerRequest request)
        {
            try
            {
                var success = await _dbService.SaveProjectEngineerAsync(
                    request.ControlNo,
                    request.EntityCode,
                    request.IsAssigned,
                    request.IsOIC,
                    request.AuditUser);

                return Ok(new { success });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving engineer: {ex.Message}");
                return StatusCode(500, ex.Message);
            }
        }
    }

    public class SaveEngineerRequest
    {
        public string ControlNo { get; set; } = "";
        public string EntityCode { get; set; } = "";
        public bool IsAssigned { get; set; }
        public bool IsOIC { get; set; }
        public string AuditUser { get; set; } = "";
    }
}
