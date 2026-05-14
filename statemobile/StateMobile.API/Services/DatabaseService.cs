using Microsoft.Data.SqlClient;
using StateMobile.API.Models;
using System.Data;
using System.Linq;

namespace StateMobile.API.Services;

public class DatabaseService : IDatabaseService
{
    private readonly IConfiguration _configuration;
    private readonly IUserContext _userContext;

    public DatabaseService(IConfiguration configuration, IUserContext userContext)
    {
        _configuration = configuration;
        _userContext = userContext;
    }

    private SqlConnection CreateConnection(string name = "DefaultConnection")
    {
        var connString = _configuration.GetConnectionString(name)
            ?? throw new InvalidOperationException($"Connection string '{name}' not found");
        return new SqlConnection(connString);
    }

    // ─── Projects ───
    public async Task<List<ProjectModel>> GetFilteredProjectsAsync(string? statusCodes, string? engineerCodes, string? modelCodes, string? sortBy, string? sortDir)
    {
        var projects = new List<ProjectModel>();
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();

            using var cmd = new SqlCommand("Lotsinventorynew.dbo.spREMS_ProjectProfile_Mobile", conn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@mode", "ProjectProfileFilter");
            cmd.Parameters.AddWithValue("@pStatusCodes", (object?)statusCodes ?? "");
            cmd.Parameters.AddWithValue("@pEngineerCodes", (object?)engineerCodes ?? "");
            cmd.Parameters.AddWithValue("@pModelCodes", (object?)modelCodes ?? "");
            cmd.Parameters.AddWithValue("@pSortBy", sortBy ?? "projects");
            cmd.Parameters.AddWithValue("@pSortDir", sortDir ?? "ASC");

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                try {
                    projects.Add(MapProject(reader));
                } catch { }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ GetFilteredProjects error: {ex.Message}");
        }
        return projects;
    }

    public async Task<List<ProjectModel>> GetProjectsAsync()
    {
        var projects = new List<ProjectModel>();
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();

            using var cmd = new SqlCommand("Lotsinventorynew.dbo.spREMS_ProjectProfile_Mobile", conn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@mode", "ProjectProfileHDR");

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                try {
                    projects.Add(MapProject(reader));
                } catch { }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ GetProjects error: {ex.Message}");
        }
        return projects;
    }

    public async Task<ProjectModel?> GetProjectByControlNoAsync(string controlNo)
    {
        var projects = await GetProjectsAsync();
        return projects.FirstOrDefault(p => p.CtrlNo == controlNo);
    }

    private ProjectModel MapProject(SqlDataReader reader)
    {
        return new ProjectModel
        {
            WorkType = SafeGetInt(reader, "WorkType", 1),
            CtrlNo = SafeGetString(reader, "CtrlNo"),
            Particulars = SafeGetString(reader, "Particulars"),
            ProjName = SafeGetString(reader, "ProjName") ?? SafeGetString(reader, "Particulars") ?? "",
            StatusText = SafeGetString(reader, "StatusText", "Active"),
            AssignedEngineersOICNames = SafeGetString(reader, "AssignedEngineersOICNames"),
            AssignedEngineersOIC = SafeGetString(reader, "AssignedEngineersOIC") ?? SafeGetString(reader, "AssignedEngineerOIC") ?? "", // Added
            AssignedEngineerCode = SafeGetString(reader, "AssignedEngineersCode") ?? SafeGetString(reader, "AssignedEngineerCode") ?? "",
            StatusCode = SafeGetInt(reader, "StatusCode", 0),
            PercentageCompletion = SafeGetDecimal(reader, "PercentageCompletion", 0),
            GC = SafeGetString(reader, "Contractor") ?? SafeGetString(reader, "GC") ?? "",
            AwardDate = SafeGetDateTime(reader, "ActualAwardDate") ?? SafeGetDateTime(reader, "AwardDate"), // Map to ActualAwardDate
            TargetEndDate = SafeGetDateTime(reader, "LatestTargetDate") ?? SafeGetDateTime(reader, "TargetEndDateFormatted") ?? SafeGetDateTime(reader, "TargetEndDate"), // Map to LatestTargetDate
            PrepDate = SafeGetDateTime(reader, "PrepDate"),
            TargetStartDate = SafeGetDateTime(reader, "TargetStartDate"),
            ActualStartDate = SafeGetDateTime(reader, "ActualDateStart"),
            ActualDateCompletion = SafeGetDateTime(reader, "DateCompleted"),
            AssignedEngineers = SafeGetString(reader, "AssignedEngineers"),
            ModelCode = SafeGetInt(reader, "ModelCode"),
            ModelName = SafeGetString(reader, "ModelName")
        };
    }

    private string SafeGetString(SqlDataReader reader, string columnName, string @default = "")
    {
        try {
            if (!reader.HasColumn(columnName) || reader[columnName] == DBNull.Value) return @default;
            return reader[columnName].ToString()?.Trim() ?? @default;
        } catch { return @default; }
    }

    private int SafeGetInt(SqlDataReader reader, string columnName, int @default = 0)
    {
        try {
            if (!reader.HasColumn(columnName) || reader[columnName] == DBNull.Value) return @default;
            return Convert.ToInt32(reader[columnName]);
        } catch { return @default; }
    }

    private decimal SafeGetDecimal(SqlDataReader reader, string columnName, decimal @default = 0)
    {
        try {
            if (!reader.HasColumn(columnName) || reader[columnName] == DBNull.Value) return @default;
            return Convert.ToDecimal(reader[columnName]);
        } catch { return @default; }
    }

    private DateTime? SafeGetDateTime(SqlDataReader reader, string columnName)
    {
        try {
            if (!reader.HasColumn(columnName) || reader[columnName] == DBNull.Value) return null;
            var val = reader[columnName];
            if (val is DateTime dt) return dt;
            if (DateTime.TryParse(val.ToString(), out var result)) return result;
            return null;
        } catch { return null; }
    }

    public async Task<List<HouseModelFilterModel>> GetHouseModelListAsync()
    {
        var list = new List<HouseModelFilterModel>();
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();
            using var cmd = new SqlCommand("Lotsinventorynew.dbo.spREMS_ProjectProfile_Mobile", conn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@mode", "ModelList");
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new HouseModelFilterModel
                {
                    Code = SafeGetInt(reader, "Code"),
                    Name = SafeGetString(reader, "Name")
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ GetHouseModelList error: {ex.Message}");
        }
        return list;
    }

    // ─── Project Profile Diary (Updates) ───

    public async Task<List<ProjectDiaryModel>> GetProjectDiaryAsync(string controlNo, string startDate = "", string endDate = "", string auditUser = "")
    {
        var entries = new List<ProjectDiaryModel>();
        try
        {
            DateTime? parsedStartDate = null;
            DateTime? parsedEndDate = null;
            if (!string.IsNullOrEmpty(startDate) && DateTime.TryParse(startDate, out var sd)) parsedStartDate = sd;
            if (!string.IsNullOrEmpty(endDate) && DateTime.TryParse(endDate, out var ed)) parsedEndDate = ed;
            entries = await FetchDiaryFromSP("Lotsinventorynew.dbo.spREMS_ProjectProfile", controlNo, parsedStartDate, parsedEndDate, auditUser);
            if (entries.Count == 0) entries = await FetchDiaryFromSP("Lotsinventorynew.dbo.spREMS_ProjectProfile_Mobile", controlNo, parsedStartDate, parsedEndDate, auditUser);
            if (entries.Count > 0 && (parsedStartDate.HasValue || parsedEndDate.HasValue))
            {
                entries = entries.Where(e => 
                    (!parsedStartDate.HasValue || e.DiaryDate.Date >= parsedStartDate.Value.Date) &&
                    (!parsedEndDate.HasValue || e.DiaryDate.Date <= parsedEndDate.Value.Date)
                ).ToList();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ GetProjectDiary error: {ex.Message}");
        }
        return entries;
    }

    private async Task<List<ProjectDiaryModel>> FetchDiaryFromSP(string spName, string controlNo, DateTime? startDate, DateTime? endDate, string auditUser)
    {
        var entries = new List<ProjectDiaryModel>();
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();
            using var cmd = new SqlCommand(spName, conn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@CtrlNo", controlNo);
            cmd.Parameters.AddWithValue("@mode", "ProjectProfileDiary");
            cmd.Parameters.AddWithValue("@DiaryViewAllAccess", "1");
            cmd.Parameters.AddWithValue("@AuditUser", auditUser ?? "");
            cmd.Parameters.Add("@DiaryStartDateFilter", SqlDbType.DateTime).Value = startDate.HasValue ? (object)startDate.Value : DBNull.Value;
            cmd.Parameters.Add("@DiaryEndDateFilter", SqlDbType.DateTime).Value = endDate.HasValue ? (object)endDate.Value : DBNull.Value;
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                entries.Add(new ProjectDiaryModel
                {
                    Id = reader["Id"] != DBNull.Value ? Convert.ToInt32(reader["Id"]) : 0,
                    ControlNo = reader["ControlNo"]?.ToString() ?? "",
                    DiaryDate = reader.HasColumn("DiaryDate") && reader["DiaryDate"] != DBNull.Value 
                        ? Convert.ToDateTime(reader["DiaryDate"])
                        : reader.HasColumn("DiaryDateFormattedJS") && reader["DiaryDateFormattedJS"] != DBNull.Value
                            ? DateTime.TryParse(reader["DiaryDateFormattedJS"].ToString(), out var d) ? d : DateTime.Now
                            : DateTime.Now,
                    DiaryDateFormatted = reader.HasColumn("DiaryDateFormatted") ? reader["DiaryDateFormatted"]?.ToString() ?? "" : "",
                    DiaryWeather = reader.HasColumn("DiaryWeather") && reader["DiaryWeather"] != DBNull.Value ? Convert.ToInt32(reader["DiaryWeather"]) : 0,
                    DiaryWeatherRemarks = reader.HasColumn("DiaryWeatherRemarks") ? reader["DiaryWeatherRemarks"]?.ToString() ?? "" : "",
                    Manpower = reader.HasColumn("Manpower") && reader["Manpower"] != DBNull.Value ? reader["Manpower"].ToString() ?? "0" : "0",
                    DiaryActivities = reader["DiaryActivities"]?.ToString() ?? "",
                    AuditUser = reader["AuditUser"]?.ToString() ?? "",
                    AuditDateFormatted = reader.HasColumn("AuditDateFormatted") ? reader["AuditDateFormatted"]?.ToString() ?? "" : ""
                });
            }
        }
        catch { }
        return entries;
    }

    public async Task<int> SaveProjectDiaryAsync(SaveDiaryRequest request)
    {
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();
            using var cmd = new SqlCommand("Lotsinventorynew.dbo.spREMS_ProjectProfile_Mobile", conn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@mode", "SaveDiary");
            cmd.Parameters.AddWithValue("@CtrlNo", request.ControlNo);
            cmd.Parameters.AddWithValue("@DiaryEntryID", request.DiaryEntryID);
            cmd.Parameters.Add("@DiaryDate", SqlDbType.DateTime).Value = DateTime.Parse(request.DiaryDate);
            cmd.Parameters.AddWithValue("@DiaryWeather", request.DiaryWeather);
            cmd.Parameters.AddWithValue("@DiaryWeatherRemarks", request.DiaryWeatherRemarks ?? "");
            cmd.Parameters.AddWithValue("@Manpower", request.Manpower ?? "0");
            cmd.Parameters.AddWithValue("@DiaryActivities", request.DiaryActivities ?? "");
            
            string auditUser = _userContext.UserID ?? request.AuditUser ?? "MobileUser";
            cmd.Parameters.AddWithValue("@AuditUser", auditUser);
            cmd.Parameters.Add("@AuditDate", SqlDbType.DateTime).Value = DateTime.Now;
            var result = await cmd.ExecuteScalarAsync();
            return result != null ? Convert.ToInt32(result) : 0;
        } catch { throw; }
    }

    public async Task<int> UpdateProjectDiaryAsync(SaveDiaryRequest request) { return await SaveProjectDiaryAsync(request); }
    public async Task<bool> DeleteProjectDiaryAsync(int diaryEntryId)
    {
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();
            
            System.Diagnostics.Debug.WriteLine($"[API] 🗑️ Targeted cascade delete for Entry ID: {diaryEntryId}");

            // 1. Strictly Cascade Delete ONLY associated files that are explicitly tagged with this Entry ID.
            // We use CHARINDEX instead of LIKE because brackets [] are special characters in SQL LIKE patterns.
            var searchTag = $"[EntryID:{diaryEntryId}]";
            
            var deleteByTagQuery = "DELETE FROM Lotsinventorynew.dbo.trx_ProjectProfileDiaryFiles WHERE CHARINDEX(@Tag, FileDescription) > 0";
            using (var cmdFiles = new SqlCommand(deleteByTagQuery, conn))
            {
                cmdFiles.Parameters.AddWithValue("@Tag", searchTag);
                int removed = await cmdFiles.ExecuteNonQueryAsync();
                System.Diagnostics.Debug.WriteLine($"[API] 🗑️ Strictly removed {removed} files matching tag '{searchTag}'");
            }

            // 2. Delete the diary entry itself via stored procedure
            using var cmd = new SqlCommand("Lotsinventorynew.dbo.spREMS_ProjectProfile_Mobile", conn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@mode", "DeleteDiary");
            cmd.Parameters.AddWithValue("@DiaryEntryID", diaryEntryId);
            await cmd.ExecuteNonQueryAsync();
            
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[API] ❌ DeleteProjectDiaryAsync error: {ex.Message}");
            return false;
        }
    }

    public async Task<List<ProjectDiaryFileModel>> GetProjectDiaryFilesAsync(string controlNo, string startDate = "", string endDate = "", string auditUser = "")
    {
        var files = new List<ProjectDiaryFileModel>();
        try {
            using var conn = CreateConnection();
            await conn.OpenAsync();
            using var cmd = new SqlCommand("Lotsinventorynew.dbo.spREMS_ProjectProfile_Mobile", conn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@CtrlNo", controlNo);
            cmd.Parameters.AddWithValue("@mode", "ProjectProfileDiaryFiles");
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) {
                files.Add(new ProjectDiaryFileModel { 
                    Id = Convert.ToInt32(reader["Id"]), 
                    DiaryID = reader.HasColumn("DiaryIDLink") && reader["DiaryIDLink"] != DBNull.Value ? Convert.ToInt32(reader["DiaryIDLink"]) : 0,
                    FileName = reader["FileName"]?.ToString() ?? "",
                    StreamID = reader.HasColumn("StreamID") ? reader["StreamID"]?.ToString() ?? "" : "",
                    DiaryDateFormatted = reader.HasColumn("DiaryDateFormatted") ? reader["DiaryDateFormatted"]?.ToString() ?? "" : "",
                    FileContentType = reader.HasColumn("FileContentType") ? reader["FileContentType"]?.ToString() ?? "" : "",
                    FileDescription = reader.HasColumn("FileDescription") ? reader["FileDescription"]?.ToString() ?? "" : "",
                    AuditUser = reader.HasColumn("AuditUser") ? reader["AuditUser"]?.ToString() ?? "" : "",
                    AuditDateFormatted = reader.HasColumn("AuditDateFormatted") ? reader["AuditDateFormatted"]?.ToString() ?? "" : ""
                });
            }
        } catch { }
        return files;
    }

    public async Task<(byte[]? fileBytes, string fileName, string contentType)> GetDiaryFileContentAsync(string streamId)
    {
        try {
            using var conn = CreateConnection();
            await conn.OpenAsync();
            var query = "SELECT FileName, FileContentType, FileData FROM Lotsinventorynew.dbo.trx_ProjectProfileDiaryFiles WHERE StreamID = @streamid AND FileData IS NOT NULL";
            using (var cmdLocal = new SqlCommand(query, conn)) {
                cmdLocal.Parameters.AddWithValue("@streamid", streamId);
                using var localReader = await cmdLocal.ExecuteReaderAsync();
                if (await localReader.ReadAsync()) return ((byte[])localReader["FileData"], localReader["FileName"].ToString() ?? "file", localReader["FileContentType"].ToString() ?? "image/jpeg");
            }
            using var cmd = new SqlCommand("Lotsinventorynew.dbo.spREMS_ProjectProfile_Mobile", conn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@mode", "GetFileOnSGC");
            cmd.Parameters.AddWithValue("@streamid", streamId);
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync()) return (reader["file_stream"] != DBNull.Value ? (byte[])reader["file_stream"] : null, reader["FileName"].ToString() ?? "file", reader["FileContentType"].ToString() ?? "image/jpeg");
        } catch { }
        return (null, "", "");
    }

    public async Task<bool> SaveProjectDiaryFileAsync(SaveDiaryFileRequest request)
    {
        try {
            using var conn = CreateConnection();
            await conn.OpenAsync();
            byte[] fileContent = Convert.FromBase64String(request.FileContentBase64);
            var query = @"DECLARE @DiaryFileID int; SELECT @DiaryFileID = ISNULL(MAX(Id), 0) + 1 FROM Lotsinventorynew.dbo.trx_ProjectProfileDiaryFiles; 
                        INSERT INTO Lotsinventorynew.dbo.trx_ProjectProfileDiaryFiles 
                        (Id, ControlNo, DiaryDate, DiaryIDLink, FileName, FileContentType, FileDescription, StreamID, FileData, AuditUser, AuditDate) 
                        VALUES 
                        (@DiaryFileID, @CtrlNo, @DiaryDate, @DiaryEntryID, @filename, @contenttype, @filedescription, CONVERT(varchar(255), NEWID()), @filecontent, @AuditUser, GETDATE());";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@CtrlNo", request.ControlNo);
            cmd.Parameters.AddWithValue("@DiaryDate", request.DiaryDate);
            cmd.Parameters.AddWithValue("@DiaryEntryID", request.DiaryEntryID);
            cmd.Parameters.AddWithValue("@filename", request.FileName);
            cmd.Parameters.AddWithValue("@contenttype", request.FileContentType);
            cmd.Parameters.Add("@filecontent", SqlDbType.VarBinary, -1).Value = fileContent;
            cmd.Parameters.AddWithValue("@filedescription", request.FileDescription ?? "");
            
            string auditUser = _userContext.UserID ?? request.AuditUser ?? "MobileUser";
            cmd.Parameters.AddWithValue("@AuditUser", auditUser);
            await cmd.ExecuteNonQueryAsync();
            return true;
        } catch { return false; }
    }

    public async Task<bool> DeleteProjectDiaryFileAsync(int fileId)
    {
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();
            
            System.Diagnostics.Debug.WriteLine($"[API] 🗑️ Attempting to delete diary file ID: {fileId}");

            // Strategy 1: Try spREMS_ProjectProfile_Mobile (using @DiaryEntryID as the parameter name per SP definition)
            try
            {
                using var cmd = new SqlCommand("Lotsinventorynew.dbo.spREMS_ProjectProfile_Mobile", conn);
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@mode", "DeleteDiaryFile");
                cmd.Parameters.AddWithValue("@DiaryEntryID", fileId); // The SP uses this parameter name for deletion
                await cmd.ExecuteNonQueryAsync();
                System.Diagnostics.Debug.WriteLine($"[API] ✅ Deleted via spREMS_ProjectProfile_Mobile");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] ⚠️ spREMS_ProjectProfile_Mobile delete failed: {ex.Message}");
                
                // Strategy 2: Try spREMS_ProjectProfile (the main one)
                try
                {
                    using var cmd2 = new SqlCommand("Lotsinventorynew.dbo.spREMS_ProjectProfile", conn);
                    cmd2.CommandType = CommandType.StoredProcedure;
                    cmd2.Parameters.AddWithValue("@mode", "DeleteDiaryFile");
                    cmd2.Parameters.AddWithValue("@DiaryEntryID", fileId);
                    await cmd2.ExecuteNonQueryAsync();
                    System.Diagnostics.Debug.WriteLine($"[API] ✅ Deleted via spREMS_ProjectProfile");
                }
                catch (Exception ex2)
                {
                    System.Diagnostics.Debug.WriteLine($"[API] ⚠️ spREMS_ProjectProfile delete failed: {ex2.Message}");
                }
            }

            // Strategy 3: Direct SQL Delete as a safety fallback to ensure the record IS removed
            // This bypasses any SP logic/parameter issues.
            var query = "DELETE FROM Lotsinventorynew.dbo.trx_ProjectProfileDiaryFiles WHERE Id = @Id";
            using var cmdFallback = new SqlCommand(query, conn);
            cmdFallback.Parameters.AddWithValue("@Id", fileId);
            int rows = await cmdFallback.ExecuteNonQueryAsync();
            
            if (rows > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[API] ✅ Record removed from trx_ProjectProfileDiaryFiles (Rows affected: {rows})");
                return true;
            }

            // check if it still exists
            var checkQuery = "SELECT COUNT(1) FROM Lotsinventorynew.dbo.trx_ProjectProfileDiaryFiles WHERE Id = @Id";
            using var cmdCheck = new SqlCommand(checkQuery, conn);
            cmdCheck.Parameters.AddWithValue("@Id", fileId);
            var exists = (int)(await cmdCheck.ExecuteScalarAsync() ?? 0) > 0;
            
            if (!exists) 
            {
                System.Diagnostics.Debug.WriteLine($"[API] ✅ Verified record no longer exists.");
                return true; 
            }

            System.Diagnostics.Debug.WriteLine($"[API] ❌ Failed to delete record {fileId}. Still exists in database.");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ DeleteProjectDiaryFile error: {ex.Message}");
            return false;
        }
    }

    // ─── Notifications ───

    public async Task<List<NotificationModel>> GetNotificationsAsync(string aisno)
    {
        var list = new List<NotificationModel>();
        try {
            using var conn = CreateConnection("Notification");
            await conn.OpenAsync();
            using var cmd = new SqlCommand("spNotification_GetList", conn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@aisno", aisno);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) {
                list.Add(new NotificationModel { 
                    Code = SafeGetInt64(reader, "Code"),
                    Date = SafeGetDateTime(reader, "Date") ?? DateTime.Now,
                    ModuleName = SafeGetString(reader, "ModuleName"),
                    Message = SafeGetString(reader, "Message"),
                    RequestBy = SafeGetString(reader, "Requestby"),
                    ForApproval = SafeGetString(reader, "ForApproval"),
                    WidgetName = SafeGetString(reader, "WidgetName"),
                    ModuleCode = SafeGetInt(reader, "ModuleCode"),
                    InternetURL = SafeGetString(reader, "InternetURL"),
                    LocalURL = SafeGetString(reader, "LocalURL"),
                    URL = SafeGetString(reader, "URL"),
                    AISNo = SafeGetString(reader, "AISNo"),
                    Done = SafeGetInt(reader, "Done"),
                    DateRead = SafeGetDateTime(reader, "DateRead")
                });
            }
        } catch (Exception ex) {
            Console.WriteLine($"❌ GetNotifications error: {ex.Message}");
        }
        return list;
    }

    public async Task<object> GetUnreadCountsAsync(string aisno) 
    { 
        int notifCount = 0;
        int chatCount = 0;

        try 
        {
            using var connNotif = CreateConnection("Notification");
            await connNotif.OpenAsync();
            var queryNotif = @"
                SELECT COUNT(*) 
                FROM trx_Notification n
                INNER JOIN mst_NotificationModule nm ON n.Module = nm.Code
                WHERE n.AISNo = @aisno AND n.Done = 0 AND n.DateRead IS NULL";
            using var cmdNotif = new SqlCommand(queryNotif, connNotif);
            cmdNotif.Parameters.AddWithValue("@aisno", aisno);
            notifCount = Convert.ToInt32(await cmdNotif.ExecuteScalarAsync());
        } 
        catch (Exception ex) 
        {
            Console.WriteLine($"❌ [API] GetUnreadCounts (Notif) error: {ex.Message}");
        }

        try 
        {
            using var connChat = CreateConnection("Chat");
            await connChat.OpenAsync();
            using var cmdChat = new SqlCommand("spChat_GetUnreadCount", connChat);
            cmdChat.CommandType = CommandType.StoredProcedure;
            cmdChat.Parameters.AddWithValue("@AISNo", aisno);
            var result = await cmdChat.ExecuteScalarAsync();
            chatCount = result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
        } 
        catch (Exception ex) 
        {
            Console.WriteLine($"❌ [API] GetUnreadCounts (Chat) error: {ex.Message}");
        }

        return new { Notifications = notifCount, Chats = chatCount }; 
    }


    public async Task<bool> MarkNotificationAsReadAsync(long code) 
    { 
        try {
            using var conn = CreateConnection("Notification");
            await conn.OpenAsync();
            
            string[] idColumns = { "Code", "ID", "RefID", "ControlNo" };
            foreach (var col in idColumns)
            {
                try
                {
                    // Explicitly set Done = 0 to override any database triggers that might auto-mark as done upon reading
                    var query = $"UPDATE trx_Notification SET DateRead = GETDATE(), Done = 0 WHERE {col} = @code";
                    using var cmd = new SqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@code", code);
                    int rows = await cmd.ExecuteNonQueryAsync();
                    if (rows > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"✅ Notification marked as read using {col}={code}");
                        return true;
                    }
                }
                catch (SqlException)
                {
                    continue;
                }
            }
            System.Diagnostics.Debug.WriteLine($"❌ Failed to mark notification {code} as read: Not found in any column");
            return false;
        } catch (Exception ex) { 
            System.Diagnostics.Debug.WriteLine($"❌ MarkNotificationAsReadAsync Error: {ex.Message}");
            return false; 
        }
    }

    public async Task<bool> DeleteNotificationAsync(long code) 
    { 
        try {
            using var conn = CreateConnection("Notification");
            await conn.OpenAsync();
            
            string[] idColumns = { "Code", "ID", "RefID", "ControlNo" };
            foreach (var col in idColumns)
            {
                try
                {
                    var query = $"DELETE FROM trx_Notification WHERE {col} = @code";
                    using var cmd = new SqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@code", code);
                    int rows = await cmd.ExecuteNonQueryAsync();
                    if (rows > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"✅ Notification deleted using {col}={code}");
                        return true;
                    }
                }
                catch (SqlException)
                {
                    continue;
                }
            }
            System.Diagnostics.Debug.WriteLine($"❌ Failed to delete notification {code}: Not found in any column");
            return false;
        } catch (Exception ex) { 
            System.Diagnostics.Debug.WriteLine($"❌ DeleteNotificationAsync Error: {ex.Message}");
            return false; 
        }
    }

    public async Task<bool> ArchiveNotificationAsync(long code) 
    { 
        try {
            using var conn = CreateConnection("Notification");
            await conn.OpenAsync();
            
            string[] idColumns = { "Code", "ID", "RefID", "ControlNo" };
            foreach (var col in idColumns)
            {
                try
                {
                    // Setting Done = 1 will exclude it from the active list (spNotification_GetList)
                    var query = $"UPDATE trx_Notification SET Done = 1 WHERE {col} = @code";
                    using var cmd = new SqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@code", code);
                    int rows = await cmd.ExecuteNonQueryAsync();
                    if (rows > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"✅ Notification archived using {col}={code}");
                        return true;
                    }
                }
                catch (SqlException)
                {
                    continue;
                }
            }
            System.Diagnostics.Debug.WriteLine($"❌ Failed to archive notification {code}: Not found in any column");
            return false;
        } catch (Exception ex) { 
            System.Diagnostics.Debug.WriteLine($"❌ ArchiveNotificationAsync Error: {ex.Message}");
            return false; 
        }
    }

    public async Task<bool> SendNotificationAsync(NotificationModel notification) { return false; }

    private long SafeGetInt64(SqlDataReader reader, string columnName, long @default = 0)
    {
        try {
            if (!reader.HasColumn(columnName) || reader[columnName] == DBNull.Value) return @default;
            return Convert.ToInt64(reader[columnName]);
        } catch { return @default; }
    }


    // ─── Chat Rooms ───

    public async Task<bool> MarkChatMessagesAsReadAsync(Guid roomId, string userId)
    {
        try {
            using (var conn = CreateConnection("Chat")) {
                await conn.OpenAsync();
                var query = "UPDATE Messages_Data SET IsRead = 1 WHERE RoomID = @RoomID AND SenderID != @UserID AND IsRead = 0";
                using var cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@RoomID", roomId);
                cmd.Parameters.AddWithValue("@UserID", userId);
                await cmd.ExecuteNonQueryAsync();
                return true;
            }
        } catch { return false; }
    }

    public async Task<List<ChatRoomModel>> GetUserChatRoomsAsync(string userId)
    {
        var rooms = new List<ChatRoomModel>();
        try
        {
            Console.WriteLine($"📤 [API] GetUserChatRoomsAsync for user: {userId}");
            
            using var conn = CreateConnection("Chat");
            await conn.OpenAsync();
            
            userId = userId?.Trim() ?? string.Empty;

            // ✅ Use stored procedure which is battle-tested
            using var cmd = new SqlCommand("spChat_GetUserRooms", conn)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 30
            };
            cmd.Parameters.AddWithValue("@UserID", userId);

            Console.WriteLine($"⚙️ [API] Calling spChat_GetUserRooms for {userId}...");
            using var reader = await cmd.ExecuteReaderAsync();
            int rowCount = 0;
            while (await reader.ReadAsync())
            {
                rowCount++;
                var roomId = (Guid)reader["RoomID"];
                var roomName = reader["RoomName"]?.ToString()?.Trim() ?? "Untitled Room";
                var lastMsg = reader["LastMessage"]?.ToString()?.Trim() ?? "(No messages)";
                var isGroup = reader["IsGroup"] != DBNull.Value && Convert.ToBoolean(reader["IsGroup"]);
                var otherUserID = reader["OtherUserID"]?.ToString()?.Trim() ?? "";
                
                Console.WriteLine($"  📨 Room {roomId}: '{roomName}' | IsGroup={isGroup} | OtherUserID='{otherUserID}' | '{lastMsg.Substring(0, Math.Min(30, lastMsg.Length))}...'");
                
                rooms.Add(new ChatRoomModel
                {
                    RoomID = roomId,
                    RoomName = roomName,
                    OtherUserID = otherUserID,
                    IsGroup = isGroup,
                    IsOnline = reader["IsOnline"] != DBNull.Value && Convert.ToBoolean(reader["IsOnline"]),
                    LastSeen = reader["LastSeen"] != DBNull.Value ? (DateTime)reader["LastSeen"] : DateTime.MinValue,
                    LastMessage = lastMsg,
                    LastMessageTime = reader["LastMessageTime"] != DBNull.Value ? (DateTime)reader["LastMessageTime"] : DateTime.Now,
                    LastMessageSenderId = "",
                    UnreadCount = reader["UnreadCount"] != DBNull.Value ? Convert.ToInt32(reader["UnreadCount"]) : 0,
                    RoomPhoto = reader.HasColumn("RoomPhoto") ? reader["RoomPhoto"]?.ToString() ?? "" : "",
                    OtherUserPhoto = ""  // Will be filled below
                });
            }
            
            // ✅ Fetch photos for all other users
            foreach (var room in rooms)
            {
                // ✅ For group chats, ensure RoomPhoto is loaded if not already returned by SP
                if (room.IsGroup && string.IsNullOrEmpty(room.RoomPhoto))
                {
                    try
                    {
                        using var roomConn = CreateConnection("Chat");
                        await roomConn.OpenAsync();
                        var roomPhotoQuery = "SELECT RoomPhoto FROM ChatRooms WHERE RoomID = @RoomID";
                        using var roomPhotoCmd = new SqlCommand(roomPhotoQuery, roomConn);
                        roomPhotoCmd.Parameters.AddWithValue("@RoomID", room.RoomID);
                        var photo = await roomPhotoCmd.ExecuteScalarAsync();
                        room.RoomPhoto = photo?.ToString() ?? "";
                    }
                    catch { }
                }

                // ✅ For one-on-one chats, ensure we have the OtherUserID
                if (!room.IsGroup && string.IsNullOrEmpty(room.OtherUserID))
                {
                    try
                    {
                        using var otherUserConn = CreateConnection("Chat");
                        await otherUserConn.OpenAsync();
                        
                        // Fetch the OTHER participant (not the current user)
                        var otherUserQuery = "SELECT TOP 1 UserID FROM ChatParticipants WHERE RoomID = @RoomID AND UserID != @CurrentUserID ORDER BY JoinedAt ASC";
                        using var otherUserCmd = new SqlCommand(otherUserQuery, otherUserConn);
                        otherUserCmd.Parameters.AddWithValue("@RoomID", room.RoomID);
                        otherUserCmd.Parameters.AddWithValue("@CurrentUserID", userId);
                        var otherUserId = await otherUserCmd.ExecuteScalarAsync();
                        
                        if (otherUserId != null)
                        {
                            room.OtherUserID = otherUserId.ToString()?.Trim() ?? "";
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  ❌ Failed to fetch OtherUserID: {ex.Message}");
                    }
                }

                // ✅ Now fetch the photo using the OtherUserID
                if (!room.IsGroup && !string.IsNullOrEmpty(room.OtherUserID))
                {
                    try
                    {
                        using var photoConn = CreateConnection("MISD");
                        await photoConn.OpenAsync();
                        
                        // ✅ Single query join to get photo by UserID
                        var photoQuery = @"
                            SELECT TOP 1 up.Photo
                            FROM MISD.dbo.Users u
                            LEFT JOIN [State].dbo.UserProfile up ON u.AISNo = up.AISNo
                            WHERE u.UserID = @UserID OR u.AISNo = @UserID";
                        
                        using var photoCmd = new SqlCommand(photoQuery, photoConn);
                        photoCmd.Parameters.AddWithValue("@UserID", room.OtherUserID.Trim());
                        var photo = await photoCmd.ExecuteScalarAsync();
                        
                        if (photo is byte[] photoBytes && photoBytes.Length > 0)
                        {
                            room.OtherUserPhoto = Convert.ToBase64String(photoBytes);
                        }
                        else if (photo != null && !string.IsNullOrEmpty(photo.ToString()))
                        {
                            room.OtherUserPhoto = photo.ToString();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"    ❌ Photo fetch error for {room.OtherUserID}: {ex.Message}");
                    }
                }
            }
            
            Console.WriteLine($"✅ [API] Retrieved {rowCount} chat rooms with photos");
        }
        catch (Exception ex) 
        { 
            Console.WriteLine($"❌ [API] GetUserChatRoomsAsync error: {ex.GetType().Name} - {ex.Message}");
            Console.WriteLine($"❌ [API] Stack trace: {ex.StackTrace}");
        }
        return rooms;
    }

    public async Task<ChatRoomModel?> GetChatRoomAsync(Guid roomId, string? userId = null)
    {
        try {
            using var conn = CreateConnection("Chat");
            await conn.OpenAsync();
            
            // ✅ First, get basic room info
            var query = "SELECT RoomID, RoomName, IsGroup, RoomPhoto FROM ChatRooms WHERE RoomID = @RoomID";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@RoomID", roomId);
            using var reader = await cmd.ExecuteReaderAsync();
            
            if (await reader.ReadAsync()) 
            {
                var room = new ChatRoomModel 
                { 
                    RoomID = (Guid)reader["RoomID"], 
                    RoomName = reader["RoomName"]?.ToString()?.Trim() ?? "", 
                    IsGroup = reader["IsGroup"] != DBNull.Value && Convert.ToBoolean(reader["IsGroup"]), 
                    RoomPhoto = reader["RoomPhoto"]?.ToString() ?? "" 
                };
                
                // ✅ For one-on-one chats, fetch the OtherUserID and OtherUserPhoto
                if (!room.IsGroup)
                {
                    try
                    {
                        // Need to get OtherUserID from ChatParticipants
                        var participantQuery = !string.IsNullOrEmpty(userId) 
                            ? "SELECT TOP 1 UserID FROM ChatParticipants WHERE RoomID = @RoomID AND UserID != @RequesterID ORDER BY JoinedAt ASC"
                            : "SELECT TOP 1 UserID FROM ChatParticipants WHERE RoomID = @RoomID ORDER BY JoinedAt ASC";
                            
                        using var participantCmd = new SqlCommand(participantQuery, conn);
                        participantCmd.Parameters.AddWithValue("@RoomID", roomId);
                        if (!string.IsNullOrEmpty(userId))
                            participantCmd.Parameters.AddWithValue("@RequesterID", userId);
                            
                        var otherUserId = await participantCmd.ExecuteScalarAsync();
                        
                        if (otherUserId != null)
                        {
                            room.OtherUserID = otherUserId.ToString()?.Trim() ?? "";
                            
                            // ✅ Now fetch the photo for this user using single join
                            if (!string.IsNullOrEmpty(room.OtherUserID))
                            {
                                try
                                {
                                    using var photoConn = CreateConnection("MISD");
                                    await photoConn.OpenAsync();
                                    
                                    var photoQuery = @"
                                        SELECT TOP 1 up.Photo
                                        FROM MISD.dbo.Users u
                                        LEFT JOIN [State].dbo.UserProfile up ON u.AISNo = up.AISNo
                                        WHERE u.UserID = @UserID OR u.AISNo = @UserID";
                                    
                                    using var photoCmd = new SqlCommand(photoQuery, photoConn);
                                    photoCmd.Parameters.AddWithValue("@UserID", room.OtherUserID.Trim());
                                    var photo = await photoCmd.ExecuteScalarAsync();
                                    
                                    if (photo is byte[] photoBytes && photoBytes.Length > 0)
                                    {
                                        room.OtherUserPhoto = Convert.ToBase64String(photoBytes);
                                    }
                                    else if (photo != null && !string.IsNullOrEmpty(photo.ToString()))
                                    {
                                        room.OtherUserPhoto = photo.ToString();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"  ⚠️  Photo error for {room.OtherUserID}: {ex.Message}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  ⚠️  OtherUserID fetch error: {ex.Message}");
                    }
                }
                
                return room;
            }
        } 
        catch (Exception ex)
        { 
            Console.WriteLine($"❌ [API] GetChatRoomAsync Error: {ex.Message}");
        }
        return null;
    }

    public async Task<List<ChatMessageModel>> GetChatMessagesAsync(Guid roomId, string? userId = null)
    {
        var page = await GetChatMessagesPageAsync(roomId, userId, null, null, 50);
        return page.Messages;
    }

    public async Task<ChatMessageContentResponse?> GetChatMessageContentAsync(long messageId)
    {
        try
        {
            using var conn = CreateConnection("Chat");
            await conn.OpenAsync();

            const string query = @"
                SELECT TOP 1
                    m.MessageID,
                    m.EncryptedText
                FROM Messages_Data m
                WHERE m.MessageID = @MessageID;";

            using var cmd = new SqlCommand(query, conn);
            cmd.CommandTimeout = 30;
            cmd.Parameters.AddWithValue("@MessageID", messageId);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                try
                {
                    if (reader[1] != DBNull.Value)
                    {
                        var messageBytes = (byte[])reader[1];
                        var messageText = System.Text.Encoding.Unicode.GetString(messageBytes);
                        return new ChatMessageContentResponse
                        {
                            MessageID = Convert.ToInt64(reader[0]),
                            MessageText = messageText
                        };
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Failed to decode message content: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [API] GetChatMessageContentAsync error: {ex.GetType().Name} - {ex.Message}");
        }

        return null;
    }

    public async Task<ChatMessagesPageResult> GetChatMessagesPageAsync(Guid roomId, string? userId = null, long? beforeMessageId = null, DateTime? beforeTimestamp = null, int pageSize = 50)
    {
        var result = new ChatMessagesPageResult();
        try
        {
            Console.WriteLine($"📥 [API] GetChatMessagesPage RoomID={roomId}, UserID={userId ?? "null"}, BeforeMessageId={beforeMessageId}, BeforeTimestamp={beforeTimestamp}, PageSize={pageSize}");

            if (pageSize <= 0) pageSize = 50;
            if (pageSize > 100) pageSize = 100;

            using var conn = CreateConnection("Chat");
            await conn.OpenAsync();

            var archivedAt = DateTime.MinValue;
            using (var archivedCmd = new SqlCommand("SELECT ISNULL(LastDeletedAt, '1900-01-01') FROM ChatParticipants WHERE RoomID = @RoomID AND UserID = @UserID", conn))
            {
                archivedCmd.Parameters.AddWithValue("@RoomID", roomId);
                archivedCmd.Parameters.AddWithValue("@UserID", (object?)userId ?? DBNull.Value);
                using var archivedReader = await archivedCmd.ExecuteReaderAsync();
                if (await archivedReader.ReadAsync())
                {
                    archivedAt = Convert.ToDateTime(archivedReader[0]);
                }
            }

            Console.WriteLine($"📊 [API] Room {roomId} for User {userId}: LastDeletedAt={archivedAt:O}");


            var query = @"
                SELECT TOP (@TakePlusOne)
                    m.MessageID,
                    m.RoomID,
                    m.SenderID,
                    m.EncryptedText,
                    m.Timestamp,
                    m.IsRead
                FROM Messages_Data m
                LEFT JOIN MessageDeletions md ON m.MessageID = md.MessageID AND md.UserID = @UserID
                WHERE m.RoomID = @RoomID
                  AND md.MessageID IS NULL
                  AND m.Timestamp > @ArchivedAt
                  AND (@BeforeTimestamp IS NULL OR m.Timestamp < @BeforeTimestamp OR (m.Timestamp = @BeforeTimestamp AND m.MessageID < @BeforeMessageId))
                ORDER BY m.Timestamp DESC, m.MessageID DESC;";

            using var cmd = new SqlCommand(query, conn);
            cmd.CommandTimeout = 30;
            cmd.Parameters.AddWithValue("@RoomID", roomId);
            cmd.Parameters.AddWithValue("@ArchivedAt", archivedAt);
            cmd.Parameters.AddWithValue("@UserID", (object?)userId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@TakePlusOne", pageSize + 1);
            cmd.Parameters.AddWithValue("@BeforeTimestamp", (object?)beforeTimestamp ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@BeforeMessageId", (object?)beforeMessageId ?? DBNull.Value);

            var rows = new List<ChatMessageModel>();
            int messageCount = 0;
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    messageCount++;
                    
                    var msg = new ChatMessageModel
                    {
                        MessageID = Convert.ToInt64(reader[0]),
                        RoomID = (Guid)reader[1],
                        SenderID = reader[2]?.ToString() ?? "",
                        MessageText = "",
                        Timestamp = (DateTime)reader[4],
                        IsRead = Convert.ToBoolean(reader[5])
                    };

                    if (reader[3] != DBNull.Value)
                    {
                        try
                        {
                            string messageText = "";
                            
                            // Decrypt the message using SQL Server's DECRYPTBYPASSPHRASE
                            using (var decryptConn = CreateConnection("Chat"))
                            {
                                await decryptConn.OpenAsync();
                                var decryptCmd = new SqlCommand(
                                    "SELECT CAST(DECRYPTBYPASSPHRASE('MySecretKey2026', @EncryptedText) AS NVARCHAR(MAX))", 
                                    decryptConn);
                                decryptCmd.Parameters.Add("@EncryptedText", SqlDbType.VarBinary, -1).Value = (byte[])reader[3];
                                var decrypted = await decryptCmd.ExecuteScalarAsync();
                                
                                if (decrypted != null && decrypted != DBNull.Value)
                                {
                                    messageText = decrypted.ToString() ?? "";
                                }
                                else
                                {
                                    // Fallback for unencrypted (e.g. system messages or legacy data)
                                    messageText = DecodeMessageText(reader[3]);
                                }
                            }
                            
                            msg.MessageText = BuildAttachmentPreview(messageText);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"    ❌ [API] Decrypt error: {ex.Message}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"    ℹ️ [API] Message {msg.MessageID} has no encrypted text");
                    }

                    rows.Add(msg);
                }
            }
            
            Console.WriteLine($"📊 [API] Read {messageCount} messages total");

            result.HasMoreOlderMessages = rows.Count > pageSize;
            if (result.HasMoreOlderMessages)
            {
                rows.RemoveAt(rows.Count - 1);
            }

            rows.Reverse();
            result.Messages = rows;
            
            // Load reactions for all messages
            if (rows.Count > 0)
            {
                var messageIds = string.Join(",", rows.Select(r => r.MessageID));
                using (var reactCmd = new SqlCommand("spChat_GetMessageReactions", conn))
                {
                    reactCmd.CommandType = CommandType.StoredProcedure;
                    reactCmd.Parameters.AddWithValue("@MessageIDs", messageIds);
                    
                    using (var reactReader = await reactCmd.ExecuteReaderAsync())
                    {
                        var reactionMap = new Dictionary<long, List<ReactionModel>>();
                        while (await reactReader.ReadAsync())
                        {
                            var msgId = Convert.ToInt64(reactReader["MessageID"]);
                            if (!reactionMap.ContainsKey(msgId))
                                reactionMap[msgId] = new List<ReactionModel>();
                            
                            reactionMap[msgId].Add(new ReactionModel
                            {
                                ReactionID = reactReader.HasColumn("ReactionID") ? Convert.ToInt64(reactReader["ReactionID"]) : 0,
                                MessageID = msgId,
                                UserID = reactReader["UserID"]?.ToString() ?? "",
                                ReactionType = reactReader["ReactionType"]?.ToString() ?? ""
                            });
                        }
                        
                        // Assign reactions to messages
                        foreach (var row in rows)
                        {
                            if (reactionMap.TryGetValue(row.MessageID, out var reactions))
                            {
                                row.Reactions = reactions;
                                Console.WriteLine($"    ❤️ [API] Loaded {reactions.Count} reactions for message {row.MessageID}");
                            }
                        }
                    }
                }
            }
            
            if (rows.Count > 0)
            {
                result.OldestMessageId = rows[0].MessageID;
                result.OldestTimestamp = rows[0].Timestamp;
            }

            Console.WriteLine($"✅ [API] GetChatMessagesPage got {result.Messages.Count} messages, hasMore={result.HasMoreOlderMessages}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [API] GetChatMessagesPage error: {ex.GetType().Name} - {ex.Message}");
        }

        return result;
    }

    private static string DecodeMessageText(object value)
    {
        try
        {
            if (value == DBNull.Value || value == null) return string.Empty;
            return value is byte[] bytes
                ? System.Text.Encoding.Unicode.GetString(bytes)
                : value.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string BuildAttachmentPreview(string rawMessageText)
    {
        if (string.IsNullOrWhiteSpace(rawMessageText)) return string.Empty;

        var text = StripSystemPrefixes(rawMessageText);
        if (TryGetAttachmentHeader(text, out var attachmentType, out var fileName))
        {
            return $"[{attachmentType}:{fileName}]";
        }

        return text;
    }

    private static string StripSystemPrefixes(string text)
    {
        var workingText = text;
        var changed = true;

        while (changed)
        {
            changed = false;

            if (workingText.StartsWith("[REPLY:"))
            {
                var firstColon = workingText.IndexOf(':');
                var secondColon = workingText.IndexOf(':', firstColon + 1);
                var thirdColon = workingText.IndexOf(':', secondColon + 1);

                if (thirdColon > 0)
                {
                    var endBracket = workingText.IndexOf(']', thirdColon + 1);
                    if (endBracket >= 0)
                    {
                        workingText = workingText.Substring(endBracket + 1);
                        changed = true;
                        continue;
                    }
                }

                var simpleEnd = workingText.IndexOf(']');
                if (simpleEnd >= 0)
                {
                    workingText = workingText.Substring(simpleEnd + 1);
                    changed = true;
                }
            }

            if (workingText.StartsWith("[FWD]"))
            {
                workingText = workingText.Substring(5);
                changed = true;
            }
        }

        return workingText;
    }

    private static bool TryGetAttachmentHeader(string text, out string attachmentType, out string fileName)
    {
        attachmentType = string.Empty;
        fileName = string.Empty;

        if (!(text.StartsWith("[FILE:") || text.StartsWith("[IMG:") || text.StartsWith("[AUDIO:")))
        {
            return false;
        }

        var closingBracket = text.IndexOf(']');
        if (closingBracket <= 0)
        {
            return false;
        }

        var header = text.Substring(1, closingBracket - 1);
        var colonIndex = header.IndexOf(':');
        if (colonIndex <= 0)
        {
            return false;
        }

        attachmentType = header.Substring(0, colonIndex);
        fileName = header.Substring(colonIndex + 1);
        return !string.IsNullOrWhiteSpace(attachmentType);
    }

    public async Task<long> SendChatMessageAsync(Guid roomId, string senderId, string messageText)
    {
        try {
            using var conn = CreateConnection("Chat");
            await conn.OpenAsync();
            // ✅ Use ENCRYPTBYPASSPHRASE to encrypt message (matches schema design)
            var query = "INSERT INTO Messages_Data (RoomID, SenderID, EncryptedText, Timestamp, IsRead) VALUES (@RoomID, @SenderID, ENCRYPTBYPASSPHRASE('MySecretKey2026', @Text), GETDATE(), 0); SELECT SCOPE_IDENTITY();";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@RoomID", roomId);
            cmd.Parameters.AddWithValue("@SenderID", senderId);
            cmd.Parameters.AddWithValue("@Text", messageText ?? "");
            return Convert.ToInt64(await cmd.ExecuteScalarAsync());
        } catch { return -1; }
    }

    public async Task<ChatRoomModel?> GetOrCreateChatRoomAsync(string currentUserId, string targetUserId, string targetFullName)
    {
        try {
            using var conn = CreateConnection("Chat");
            await conn.OpenAsync();
            using var findCmd = new SqlCommand("spChat_FindOneOnOneRoom", conn);
            findCmd.CommandType = CommandType.StoredProcedure;
            findCmd.Parameters.AddWithValue("@CurrentUserID", currentUserId);
            findCmd.Parameters.AddWithValue("@TargetUserID", targetUserId);
            using var reader = await findCmd.ExecuteReaderAsync();
            if (await reader.ReadAsync()) 
            {
                var room = new ChatRoomModel 
                { 
                    RoomID = (Guid)reader["RoomID"], 
                    RoomName = targetFullName,
                    OtherUserID = targetUserId
                };
                reader.Close();
                
                // ✅ Fetch photo for the other user using single join
                try
                {
                    using var photoConn = CreateConnection("MISD");
                    await photoConn.OpenAsync();
                    
                    var photoQuery = @"
                        SELECT TOP 1 up.Photo
                        FROM MISD.dbo.Users u
                        LEFT JOIN [State].dbo.UserProfile up ON u.AISNo = up.AISNo
                        WHERE u.UserID = @UserID OR u.AISNo = @UserID";
                    
                    using var photoCmd = new SqlCommand(photoQuery, photoConn);
                    photoCmd.Parameters.AddWithValue("@UserID", targetUserId.Trim());
                    var photo = await photoCmd.ExecuteScalarAsync();
                    
                    if (photo is byte[] photoBytes && photoBytes.Length > 0)
                    {
                        room.OtherUserPhoto = Convert.ToBase64String(photoBytes);
                    }
                    else if (photo != null && !string.IsNullOrEmpty(photo.ToString()))
                    {
                        room.OtherUserPhoto = photo.ToString();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ⚠️  Photo error for {targetUserId}: {ex.Message}");
                }
                
                return room;
            }
            reader.Close();
            
            var nrId = Guid.NewGuid();
            var q = "INSERT INTO ChatRooms (RoomID, RoomName, CreatedAt, IsGroup, IsArchived) VALUES (@RoomID, @RoomName, GETDATE(), 0, 0); INSERT INTO ChatParticipants (RoomID, UserID, JoinedAt) VALUES (@RoomID, @CurrentUserId, GETDATE()); INSERT INTO ChatParticipants (RoomID, UserID, JoinedAt) VALUES (@RoomID, @TargetUserId, GETDATE());";
            using var cmd = new SqlCommand(q, conn);
            cmd.Parameters.AddWithValue("@RoomID", nrId);
            cmd.Parameters.AddWithValue("@RoomName", targetFullName);
            cmd.Parameters.AddWithValue("@CurrentUserId", currentUserId);
            cmd.Parameters.AddWithValue("@TargetUserId", targetUserId);
            await cmd.ExecuteNonQueryAsync();
            
            var newRoom = new ChatRoomModel 
            { 
                RoomID = nrId, 
                RoomName = targetFullName,
                OtherUserID = targetUserId
            };
            
            // ✅ Fetch photo for the new room
            try
            {
                using var photoConn = CreateConnection("MISD");
                await photoConn.OpenAsync();
                
                // ✅ Get AISNo first
                var aisnoQuery = "SELECT AISNo FROM MISD.dbo.Users WHERE UserID = @UserID";
                using var aisnoCmd = new SqlCommand(aisnoQuery, photoConn);
                aisnoCmd.Parameters.AddWithValue("@UserID", targetUserId);
                var aisno = await aisnoCmd.ExecuteScalarAsync();
                
                if (aisno != null)
                {
                    // ✅ Get Photo from State
                    var photoQuery = "SELECT ISNULL(Photo, '') FROM [State].[dbo].[UserProfile] WHERE AISNo = @AISNo";
                    using var photoCmd = new SqlCommand(photoQuery, photoConn);
                    photoCmd.Parameters.AddWithValue("@AISNo", aisno);
                    var photo = await photoCmd.ExecuteScalarAsync();
                    
                    if (photo is byte[] photoBytes)
                    {
                        newRoom.OtherUserPhoto = Convert.ToBase64String(photoBytes);
                        Console.WriteLine($"  🖼️  Photo loaded for {targetUserId} (byte array: {photoBytes.Length} bytes)");
                    }
                    else if (photo != null && !string.IsNullOrEmpty(photo.ToString()))
                    {
                        newRoom.OtherUserPhoto = photo.ToString();
                        Console.WriteLine($"  🖼️  Photo loaded for {targetUserId} (string: {newRoom.OtherUserPhoto.Length} chars)");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠️  Photo error for {targetUserId}: {ex.Message}");
            }
            
            return newRoom;
        } catch { return null; }
    }

    public async Task<ChatRoomModel?> CreateGroupChatRoomAsync(string currentUserId, List<string> targetUserIds, string roomName)
    {
        try
        {
            using var conn = CreateConnection("Chat");
            await conn.OpenAsync();

            var roomId = Guid.NewGuid();
            var creatorName = await GetUserFullNameAsync(currentUserId, conn);
            
            // If roomName is empty, generate default
            if (string.IsNullOrWhiteSpace(roomName))
            {
                var names = new List<string> { creatorName.Split(' ')[0] };
                foreach (var uid in targetUserIds.Take(2)) 
                {
                    var full = await GetUserFullNameAsync(uid, conn);
                    names.Add(full.Split(' ')[0]);
                }
                roomName = string.Join(", ", names) + (targetUserIds.Count > 2 ? "..." : "");
            }

            Console.WriteLine($"📥 [API] Creating room {roomId}: '{roomName}' with {targetUserIds.Count} others");

            // 1. Create Room
            var createRoomQuery = "INSERT INTO ChatRooms (RoomID, RoomName, CreatedAt, IsGroup, IsArchived) VALUES (@RoomID, @RoomName, GETDATE(), 1, 0)";
            using (var cmd = new SqlCommand(createRoomQuery, conn))
            {
                cmd.Parameters.AddWithValue("@RoomID", roomId);
                cmd.Parameters.AddWithValue("@RoomName", roomName);
                await cmd.ExecuteNonQueryAsync();
            }

            // 2. Add Participants
            // Creator
            using (var cmd = new SqlCommand("INSERT INTO ChatParticipants (RoomID, UserID, JoinedAt, IsAdmin) VALUES (@RoomID, @UserID, GETDATE(), 1)", conn))
            {
                cmd.Parameters.AddWithValue("@RoomID", roomId);
                cmd.Parameters.AddWithValue("@UserID", currentUserId);
                await cmd.ExecuteNonQueryAsync();
            }
            // Members
            foreach (var uid in targetUserIds)
            {
                if (string.IsNullOrWhiteSpace(uid) || uid == currentUserId) continue;
                using (var cmd = new SqlCommand("INSERT INTO ChatParticipants (RoomID, UserID, JoinedAt, IsAdmin) VALUES (@RoomID, @UserID, GETDATE(), 0)", conn))
                {
                    cmd.Parameters.AddWithValue("@RoomID", roomId);
                    cmd.Parameters.AddWithValue("@UserID", uid);
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            // 3. System Message (Optional)
            try
            {
                var systemText = $"{creatorName} created the group \"{roomName}\"";
                var msgQuery = "INSERT INTO Messages_Data (RoomID, SenderID, EncryptedText, Timestamp, IsRead) VALUES (@RoomID, 'SYSTEM', ENCRYPTBYPASSPHRASE('MySecretKey2026', @Text), GETDATE(), 0)";
                using (var cmd = new SqlCommand(msgQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@RoomID", roomId);
                    cmd.Parameters.AddWithValue("@Text", systemText);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
            catch { /* Ignore system message failure */ }

            return new ChatRoomModel
            {
                RoomID = roomId,
                RoomName = roomName,
                IsGroup = true,
                LastMessage = "Group created",
                LastMessageTime = DateTime.Now
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [API] CreateGroupChatRoomAsync ERROR: {ex.Message}");
            return null;
        }
    }

    public async Task<List<User>> SearchUsersAsync(string searchTerm, Guid? excludeRoomId = null)
    {
        var users = new List<User>();
        try {
            using var conn = CreateConnection("MISD");
            await conn.OpenAsync();
            var hasSearchTerm = !string.IsNullOrWhiteSpace(searchTerm);
            var query = hasSearchTerm
                ? @"WITH RankedUsers AS (SELECT u.UserID, u.AISNo, p.FirstName, p.LastName, os.DeptName AS DepartmentName, up.Photo, up.Nickname, up.Mobile, ROW_NUMBER() OVER (PARTITION BY p.AISNo ORDER BY u.Code DESC) AS RowNum FROM MISD.dbo.Users u INNER JOIN HRIS.dbo.PersonalInformation p ON u.AISNo = p.AISNo LEFT JOIN HRIS.dbo.mst_OrganizationalStructure os ON os.DeptCode = p.Department LEFT JOIN [State].dbo.UserProfile up ON u.AISNo = up.AISNo WHERE u.Active = 1 AND p.DateResigned IS NULL) SELECT TOP (200) UserID, AISNo, FirstName, LastName, DepartmentName, Photo, Nickname, Mobile FROM RankedUsers WHERE RowNum = 1 AND (FirstName LIKE @Term + '%' OR LastName LIKE @Term + '%' OR (FirstName + ' ' + LastName) LIKE @Term + '%' OR AISNo LIKE @Term + '%') ORDER BY LastName, FirstName"
                : @"WITH RankedUsers AS (SELECT u.UserID, u.AISNo, p.FirstName, p.LastName, os.DeptName AS DepartmentName, up.Photo, up.Nickname, up.Mobile, ROW_NUMBER() OVER (PARTITION BY p.AISNo ORDER BY u.Code DESC) AS RowNum FROM MISD.dbo.Users u INNER JOIN HRIS.dbo.PersonalInformation p ON u.AISNo = p.AISNo LEFT JOIN HRIS.dbo.mst_OrganizationalStructure os ON os.DeptCode = p.Department LEFT JOIN [State].dbo.UserProfile up ON u.AISNo = up.AISNo WHERE u.Active = 1 AND p.DateResigned IS NULL) SELECT TOP (100) UserID, AISNo, FirstName, LastName, DepartmentName, Photo, Nickname, Mobile FROM RankedUsers WHERE RowNum = 1 ORDER BY LastName, FirstName";
            using var cmd = new SqlCommand(query, conn);
            if (hasSearchTerm) cmd.Parameters.AddWithValue("@Term", searchTerm.Trim());
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) {
                users.Add(new User { 
                    UserID = reader["UserID"].ToString() ?? "", 
                    AISNo = reader["AISNo"].ToString() ?? "",
                    FirstName = reader["FirstName"].ToString() ?? "", 
                    LastName = reader["LastName"].ToString() ?? "",
                    DepartmentName = reader["DepartmentName"].ToString() ?? "",
                    Photo = reader["Photo"] != DBNull.Value ? (reader["Photo"] is byte[] b ? Convert.ToBase64String(b) : reader["Photo"].ToString()) : "",
                    Nickname = reader["Nickname"]?.ToString() ?? "",
                    Mobile = reader["Mobile"]?.ToString() ?? ""
                });
            }
        } catch { }
        return users;
    }

    public async Task<bool> DeleteChatRoomAsync(Guid roomId, string userId)
    {
        try
        {
            using var conn = CreateConnection("Chat");
            await conn.OpenAsync();
            
            // Mark as deleted for this user and set the deletion timestamp
            // This will hide the room from their list and hide previous messages
            var query = @"
                UPDATE ChatParticipants 
                SET IsDeleted = 1, 
                    LastDeletedAt = GETDATE() 
                WHERE RoomID = @RoomID AND UserID = @UserID";
                
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@RoomID", roomId);
            cmd.Parameters.AddWithValue("@UserID", userId);
            
            var rows = await cmd.ExecuteNonQueryAsync();
            Console.WriteLine($"🗑️ [API] DeleteChatRoomAsync: {rows} row(s) affected for room {roomId}, user {userId}");
            return rows > 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [API] DeleteChatRoomAsync error: {ex.Message}");
            return false;
        }
    }
    
    public async Task<UserStatusModel?> GetUserStatusAsync(string userId) { return null; }
    
    public async Task<bool> UpdateUserStatusAsync(string userId, bool isOnline, string? connectionId = null)
    {
        try
        {
            using var conn = CreateConnection("Chat");
            await conn.OpenAsync();
            
            var query = @"
                IF EXISTS (SELECT 1 FROM UserStatus WHERE UserID = @UserID)
                    UPDATE UserStatus SET IsOnline = @IsOnline, LastSeen = GETDATE(), ConnectionID = @ConnectionID, UpdatedAt = GETDATE() WHERE UserID = @UserID
                ELSE
                    INSERT INTO UserStatus (UserID, IsOnline, LastSeen, ConnectionID, UpdatedAt) VALUES (@UserID, @IsOnline, GETDATE(), @ConnectionID, GETDATE())";
            
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@UserID", userId);
            cmd.Parameters.AddWithValue("@IsOnline", isOnline);
            cmd.Parameters.AddWithValue("@ConnectionID", (object?)connectionId ?? DBNull.Value);
            
            await cmd.ExecuteNonQueryAsync();
            System.Diagnostics.Debug.WriteLine($"🟢 Updated user status: {userId} = {(isOnline ? "ONLINE" : "OFFLINE")}");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ UpdateUserStatusAsync error: {ex.Message}");
            return false;
        }
    }
    
    public async Task<bool> AddReactionAsync(long messageId, string userId, string reactionType)
    {
        try
        {
            using var conn = CreateConnection("Chat");
            await conn.OpenAsync();
            
            // Check if reaction already exists for this user and message
            var checkQuery = "SELECT COUNT(1) FROM MessageReactions WHERE MessageID = @MessageID AND UserID = @UserID";
            using (var checkCmd = new SqlCommand(checkQuery, conn))
            {
                checkCmd.Parameters.AddWithValue("@MessageID", messageId);
                checkCmd.Parameters.AddWithValue("@UserID", userId);
                
                var exists = (int)await checkCmd.ExecuteScalarAsync() > 0;
                
                if (exists)
                {
                    // Update existing reaction
                    var updateQuery = "UPDATE MessageReactions SET ReactionType = @ReactionType, CreatedAt = GETDATE() WHERE MessageID = @MessageID AND UserID = @UserID";
                    using (var updateCmd = new SqlCommand(updateQuery, conn))
                    {
                        updateCmd.Parameters.AddWithValue("@MessageID", messageId);
                        updateCmd.Parameters.AddWithValue("@UserID", userId);
                        updateCmd.Parameters.AddWithValue("@ReactionType", reactionType);
                        await updateCmd.ExecuteNonQueryAsync();
                    }
                }
                else
                {
                    // Insert new reaction
                    var insertQuery = "INSERT INTO MessageReactions (MessageID, UserID, ReactionType, CreatedAt) VALUES (@MessageID, @UserID, @ReactionType, GETDATE())";
                    using (var insertCmd = new SqlCommand(insertQuery, conn))
                    {
                        insertCmd.Parameters.AddWithValue("@MessageID", messageId);
                        insertCmd.Parameters.AddWithValue("@UserID", userId);
                        insertCmd.Parameters.AddWithValue("@ReactionType", reactionType);
                        await insertCmd.ExecuteNonQueryAsync();
                    }
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"✅ [API] Reaction saved: Message={messageId}, User={userId}, Type={reactionType}");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ [API] AddReactionAsync error: {ex.Message}");
            return false;
        }
    }
    
    public async Task<bool> RemoveReactionAsync(long messageId, string userId)
    {
        try
        {
            using var conn = CreateConnection("Chat");
            await conn.OpenAsync();
            
            var query = "DELETE FROM MessageReactions WHERE MessageID = @MessageID AND UserID = @UserID";
            using (var cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@MessageID", messageId);
                cmd.Parameters.AddWithValue("@UserID", userId);
                
                var rowsAffected = await cmd.ExecuteNonQueryAsync();
                System.Diagnostics.Debug.WriteLine($"✅ [API] Reaction removed: Message={messageId}, User={userId}, Rows={rowsAffected}");
                return rowsAffected > 0;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ [API] RemoveReactionAsync error: {ex.Message}");
            return false;
        }
    }
    
    public async Task<bool> MarkMessageAsReadAsync(long messageId, string userId)
    {
        try
        {
            using var conn = CreateConnection("Chat");
            await conn.OpenAsync();
            
            // Update the message IsRead flag
            var updateQuery = "UPDATE Messages_Data SET IsRead = 1 WHERE MessageID = @MessageID";
            using (var cmd = new SqlCommand(updateQuery, conn))
            {
                cmd.Parameters.AddWithValue("@MessageID", messageId);
                await cmd.ExecuteNonQueryAsync();
            }
            
            // Also insert/update read receipt
            var receiptQuery = @"
                IF EXISTS (SELECT 1 FROM MessageReceipts WHERE MessageID = @MessageID AND UserID = @UserID)
                    UPDATE MessageReceipts SET ReadAt = GETDATE() WHERE MessageID = @MessageID AND UserID = @UserID
                ELSE
                    INSERT INTO MessageReceipts (MessageID, UserID, ReadAt) VALUES (@MessageID, @UserID, GETDATE())";
            
            using (var receiptCmd = new SqlCommand(receiptQuery, conn))
            {
                receiptCmd.Parameters.AddWithValue("@MessageID", messageId);
                receiptCmd.Parameters.AddWithValue("@UserID", userId);
                await receiptCmd.ExecuteNonQueryAsync();
            }
            
            System.Diagnostics.Debug.WriteLine($"✅ Marked message {messageId} as read by {userId}");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ MarkMessageAsReadAsync error: {ex.Message}");
            return false;
        }
    }
    
    public async Task<(bool success, Guid roomId)> DeleteMessageAsync(long messageId, string userId, bool forEveryone)
    {
        try
        {
            using var conn = CreateConnection("Chat");
            await conn.OpenAsync();

            Guid roomId = Guid.Empty;
            string senderId = "";

            // Get RoomID and SenderID first
            using (var checkCmd = new SqlCommand("SELECT RoomID, SenderID FROM Messages_Data WHERE MessageID = @MessageID", conn))
            {
                checkCmd.Parameters.AddWithValue("@MessageID", messageId);
                using var reader = await checkCmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    roomId = (Guid)reader["RoomID"];
                    senderId = reader["SenderID"]?.ToString() ?? "";
                }
                else return (false, Guid.Empty);
            }

            if (forEveryone)
            {
                // Only allow sender to delete for everyone (or you could add admin check here)
                // if (senderId != userId && userId != "SYSTEM") { ... }

                // Hard delete for everyone - cascade related tables manually due to FK
                using var trans = conn.BeginTransaction();
                try
                {
                    var sql = @"
                        DELETE FROM MessageReactions WHERE MessageID = @MessageID;
                        DELETE FROM MessageReceipts WHERE MessageID = @MessageID;
                        DELETE FROM MessageDeletions WHERE MessageID = @MessageID;
                        DELETE FROM Messages_Data WHERE MessageID = @MessageID;";

                    using var cmd = new SqlCommand(sql, conn, trans);
                    cmd.Parameters.AddWithValue("@MessageID", messageId);
                    await cmd.ExecuteNonQueryAsync();
                    
                    await trans.CommitAsync();
                    return (true, roomId);
                }
                catch
                {
                    await trans.RollbackAsync();
                    throw;
                }
            }
            else
            {
                // Soft delete for just this user
                var query = @"IF NOT EXISTS (SELECT 1 FROM MessageDeletions WHERE MessageID = @MessageID AND UserID = @UserID)
                    INSERT INTO MessageDeletions (MessageID, UserID) VALUES (@MessageID, @UserID)";
                using var cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@MessageID", messageId);
                cmd.Parameters.AddWithValue("@UserID", userId);
                await cmd.ExecuteNonQueryAsync();
                return (true, roomId);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [API] DeleteMessageAsync error: {ex.Message}");
            return (false, Guid.Empty);
        }
    }

    public async Task<(bool success, Guid roomId)> DeleteMessagesAsync(List<long> messageIds, string userId, bool forEveryone)
    {
        if (messageIds == null || !messageIds.Any()) return (true, Guid.Empty);

        try
        {
            using var conn = CreateConnection("Chat");
            await conn.OpenAsync();

            // Use the first message to get the RoomID (assume all are in same room)
            Guid roomId = Guid.Empty;
            using (var checkCmd = new SqlCommand("SELECT TOP 1 RoomID FROM Messages_Data WHERE MessageID IN (" + string.Join(",", messageIds) + ")", conn))
            {
                var result = await checkCmd.ExecuteScalarAsync();
                if (result != null) roomId = (Guid)result;
                else return (false, Guid.Empty);
            }

            using var trans = conn.BeginTransaction();
            try
            {
                var idList = string.Join(",", messageIds);

                if (forEveryone)
                {
                    // Cascade delete for everyone
                    var sql = $@"
                        DELETE FROM MessageReactions WHERE MessageID IN ({idList});
                        DELETE FROM MessageReceipts WHERE MessageID IN ({idList});
                        DELETE FROM MessageDeletions WHERE MessageID IN ({idList});
                        DELETE FROM Messages_Data WHERE MessageID IN ({idList});";

                    using var cmd = new SqlCommand(sql, conn, trans);
                    await cmd.ExecuteNonQueryAsync();
                }
                else
                {
                    // Soft delete for just this user
                    foreach (var messageId in messageIds)
                    {
                        var query = @"IF NOT EXISTS (SELECT 1 FROM MessageDeletions WHERE MessageID = @MessageID AND UserID = @UserID)
                            INSERT INTO MessageDeletions (MessageID, UserID) VALUES (@MessageID, @UserID)";
                        using var cmd = new SqlCommand(query, conn, trans);
                        cmd.Parameters.AddWithValue("@MessageID", messageId);
                        cmd.Parameters.AddWithValue("@UserID", userId);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                await trans.CommitAsync();
                return (true, roomId);
            }
            catch
            {
                await trans.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [API] DeleteMessagesAsync error: {ex.Message}");
            return (false, Guid.Empty);
        }
    }
    public async Task<(bool success, string systemText, long messageId)> UpdateChatRoomAsync(Guid roomId, string roomName, string roomPhoto, string? actorId = null)
    {
        try
        {
            using var conn = CreateConnection("Chat");
            await conn.OpenAsync();

            // Get current info to compare for system message logic
            string oldName = "";
            string oldPhoto = "";
            using (var checkCmd = new SqlCommand("SELECT RoomName, RoomPhoto FROM ChatRooms WHERE RoomID = @RoomID", conn))
            {
                checkCmd.Parameters.AddWithValue("@RoomID", roomId);
                using var reader = await checkCmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    oldName = reader["RoomName"]?.ToString() ?? "";
                    oldPhoto = reader["RoomPhoto"]?.ToString() ?? "";
                }
            }

            var query = "UPDATE ChatRooms SET RoomName = @RoomName, RoomPhoto = @RoomPhoto WHERE RoomID = @RoomID";
            using (var cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@RoomID", roomId);
                cmd.Parameters.AddWithValue("@RoomName", roomName);
                cmd.Parameters.AddWithValue("@RoomPhoto", (object?)roomPhoto ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }

            // Determine system message based on what changed
            var actorName = !string.IsNullOrEmpty(actorId) ? await GetUserFullNameAsync(actorId) : "Someone";
            var systemText = "";
            
            bool nameChanged = oldName != roomName;
            bool photoChanged = oldPhoto != roomPhoto;

            if (nameChanged && photoChanged)
                systemText = $"{actorName} changed the group name and photo";
            else if (nameChanged)
                systemText = $"{actorName} changed the group name to \"{roomName}\"";
            else if (photoChanged)
                systemText = $"{actorName} changed the group photo";
            else
                return (true, "", 0); // No visible changes

            var msgQuery = @"INSERT INTO Messages_Data (RoomID, SenderID, EncryptedText, Timestamp, IsRead) VALUES (@RoomID, 'SYSTEM', ENCRYPTBYPASSPHRASE('MySecretKey2026', @Text), GETDATE(), 0); SELECT SCOPE_IDENTITY();";
            long messageId = 0;
            using (var cmd = new SqlCommand(msgQuery, conn))
            {
                cmd.Parameters.AddWithValue("@RoomID", roomId);
                cmd.Parameters.AddWithValue("@Text", systemText);
                messageId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            }

            return (true, systemText, messageId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [API] UpdateChatRoomAsync error: {ex.Message}");
            return (false, "", 0);
        }
    }

    public async Task<List<ChatParticipantModel>> GetChatParticipantsAsync(Guid roomId)
    {
        var list = new List<ChatParticipantModel>();
        try {
            using var conn = CreateConnection("Chat");
            await conn.OpenAsync();
            var query = @"SELECT p.UserID, ISNULL(pi.FirstName, '') + ' ' + ISNULL(pi.LastName, '') as FullName, up.Photo, us.IsOnline, p.IsAdmin FROM ChatParticipants p INNER JOIN MISD.dbo.Users u ON p.UserID = u.UserID INNER JOIN HRIS.dbo.PersonalInformation pi ON u.AISNo = pi.AISNo LEFT JOIN [State].dbo.UserProfile up ON pi.AISNo = up.AISNo LEFT JOIN UserStatus us ON p.UserID = us.UserID WHERE p.RoomID = @RoomID";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@RoomID", roomId);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) {
                list.Add(new ChatParticipantModel { UserID = reader["UserID"].ToString()?.Trim() ?? "", FullName = reader["FullName"].ToString()?.Trim() ?? "", Photo = reader["Photo"] != DBNull.Value ? (reader["Photo"] is byte[] b ? Convert.ToBase64String(b) : reader["Photo"].ToString()) : "", IsOnline = reader["IsOnline"] != DBNull.Value && Convert.ToBoolean(reader["IsOnline"]), IsAdmin = reader["IsAdmin"] != DBNull.Value && Convert.ToBoolean(reader["IsAdmin"]) });
            }
        } catch { }
        return list;
    }


    public async Task<(bool success, string systemText, long messageId)> UpdateParticipantRoleAsync(Guid roomId, string userId, bool isAdmin, string? actorId = null)
    {
        try
        {
            using var conn = CreateConnection("Chat");
            await conn.OpenAsync();

            var query = "UPDATE ChatParticipants SET IsAdmin = @IsAdmin WHERE RoomID = @RoomID AND UserID = @UserID";
            using (var cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@RoomID", roomId);
                cmd.Parameters.AddWithValue("@UserID", userId);
                cmd.Parameters.AddWithValue("@IsAdmin", isAdmin);
                await cmd.ExecuteNonQueryAsync();
            }

            var actorName = !string.IsNullOrEmpty(actorId) ? await GetUserFullNameAsync(actorId, conn) : "Someone";
            var targetName = await GetUserFullNameAsync(userId, conn);
            var systemText = isAdmin
                ? $"{actorName} made {targetName} an admin"
                : $"{actorName} removed {targetName} as admin";

            var msgQuery = @"INSERT INTO Messages_Data (RoomID, SenderID, EncryptedText, Timestamp, IsRead) VALUES (@RoomID, 'SYSTEM', ENCRYPTBYPASSPHRASE('MySecretKey2026', @Text), GETDATE(), 0); SELECT SCOPE_IDENTITY();";
            long messageId = 0;
            using (var cmd = new SqlCommand(msgQuery, conn))
            {
                cmd.Parameters.AddWithValue("@RoomID", roomId);
                cmd.Parameters.AddWithValue("@Text", systemText);
                messageId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            }

            return (true, systemText, messageId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [API] UpdateParticipantRoleAsync error: {ex.Message}");
            return (false, "", 0);
        }
    }

    public async Task<List<(string targetName, long messageId)>> AddParticipantsToRoomAsync(Guid roomId, List<string> userIds, string addedByUserId)
    {
        var results = new List<(string targetName, long messageId)>();
        try
        {
            using var conn = CreateConnection("Chat");
            await conn.OpenAsync();

            foreach (var userId in userIds)
            {
                if (string.IsNullOrWhiteSpace(userId)) continue;

                // Check if participant already exists
                using (var checkCmd = new SqlCommand("SELECT COUNT(1) FROM ChatParticipants WHERE RoomID = @RoomID AND UserID = @UserID", conn))
                {
                    checkCmd.Parameters.AddWithValue("@RoomID", roomId);
                    checkCmd.Parameters.AddWithValue("@UserID", userId);
                    if (Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0) continue;
                }

                // Add the participant
                using (var cmd = new SqlCommand("INSERT INTO ChatParticipants (RoomID, UserID, JoinedAt, IsAdmin) VALUES (@RoomID, @UserID, GETDATE(), 0)", conn))
                {
                    cmd.Parameters.AddWithValue("@RoomID", roomId);
                    cmd.Parameters.AddWithValue("@UserID", userId);
                    await cmd.ExecuteNonQueryAsync();
                }

                var targetName = await GetUserFullNameAsync(userId, conn);

                // Insert system message
                var msgQuery = @"INSERT INTO Messages_Data (RoomID, SenderID, EncryptedText, Timestamp, IsRead) VALUES (@RoomID, 'SYSTEM', ENCRYPTBYPASSPHRASE('MySecretKey2026', @Text), GETDATE(), 0); SELECT SCOPE_IDENTITY();";
                long messageId = 0;
                using (var cmd = new SqlCommand(msgQuery, conn))
                {
                    var addedByName = await GetUserFullNameAsync(addedByUserId, conn);
                    var text = $"{addedByName} added {targetName} to the group";
                    cmd.Parameters.AddWithValue("@RoomID", roomId);
                    cmd.Parameters.AddWithValue("@Text", text);
                    messageId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                }

                results.Add((targetName, messageId));
            }

            // Update room name if it's a default one
            await UpdateGroupChatNameIfDefaultAsync(roomId, conn);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [API] AddParticipantsToRoomAsync error: {ex.Message}");
        }
        return results;
    }

    public async Task<(string systemText, long messageId)> RemoveParticipantAsync(Guid roomId, string userId, string? actorId = null)
    {
        try
        {
            using var conn = CreateConnection("Chat");
            await conn.OpenAsync();

            var targetName = await GetUserFullNameAsync(userId, conn);
            var removedFirstName = targetName.Split(' ')[0];

            // 1. Remove the participant
            using (var cmd = new SqlCommand("DELETE FROM ChatParticipants WHERE RoomID = @RoomID AND UserID = @UserID", conn))
            {
                cmd.Parameters.AddWithValue("@RoomID", roomId);
                cmd.Parameters.AddWithValue("@UserID", userId);
                await cmd.ExecuteNonQueryAsync();
            }

            // 2. Update room name if it's a default one
            await UpdateGroupChatNameIfDefaultAsync(roomId, conn, removedFirstName);

            // 3. System message
            var actorName = !string.IsNullOrEmpty(actorId) ? await GetUserFullNameAsync(actorId, conn) : "Someone";
            var systemText = actorId == userId
                ? $"{targetName} left the group"
                : $"{actorName} removed {targetName} from the group";

            var msgQuery = @"INSERT INTO Messages_Data (RoomID, SenderID, EncryptedText, Timestamp, IsRead) VALUES (@RoomID, 'SYSTEM', ENCRYPTBYPASSPHRASE('MySecretKey2026', @Text), GETDATE(), 0); SELECT SCOPE_IDENTITY();";
            long messageId = 0;
            using (var cmd = new SqlCommand(msgQuery, conn))
            {
                cmd.Parameters.AddWithValue("@RoomID", roomId);
                cmd.Parameters.AddWithValue("@Text", systemText);
                messageId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            }

            return (systemText, messageId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [API] RemoveParticipantAsync error: {ex.Message}");
            return ("", 0);
        }
    }

    private async Task UpdateGroupChatNameIfDefaultAsync(Guid roomId, SqlConnection conn, string? removedFirstName = null)
    {
        try
        {
            string currentRoomName = "";
            using (var checkCmd = new SqlCommand("SELECT RoomName FROM ChatRooms WHERE RoomID = @RoomID AND IsGroup = 1", conn))
            {
                checkCmd.Parameters.AddWithValue("@RoomID", roomId);
                var result = await checkCmd.ExecuteScalarAsync();
                currentRoomName = result?.ToString() ?? "";
            }

            // Heuristic for default name: "Group Chat", empty, contains removed first name, or contains commas
            bool isDefaultName = string.IsNullOrWhiteSpace(currentRoomName) || 
                                 currentRoomName.Equals("Group Chat", StringComparison.OrdinalIgnoreCase) || 
                                 (!string.IsNullOrEmpty(removedFirstName) && currentRoomName.IndexOf(removedFirstName, StringComparison.OrdinalIgnoreCase) >= 0) || 
                                 currentRoomName.Contains(",");

            if (isDefaultName)
            {
                var participants = new List<string>();
                using (var partCmd = new SqlCommand(@"
                    SELECT pi.FirstName 
                    FROM ChatParticipants cp 
                    INNER JOIN MISD.dbo.Users u ON cp.UserID = u.UserID 
                    INNER JOIN HRIS.dbo.PersonalInformation pi ON u.AISNo = pi.AISNo 
                    WHERE cp.RoomID = @RoomID 
                    ORDER BY cp.JoinedAt ASC", conn))
                {
                    partCmd.Parameters.AddWithValue("@RoomID", roomId);
                    using var partReader = await partCmd.ExecuteReaderAsync();
                    while (await partReader.ReadAsync())
                    {
                        var name = partReader["FirstName"]?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(name)) participants.Add(name);
                    }
                }

                if (participants.Count > 0)
                {
                    string newName = string.Join(", ", participants.Take(3)) + (participants.Count > 3 ? "..." : "");
                    
                    // If the old name was all-caps, keep the new one all-caps for consistency
                    if (currentRoomName.Equals(currentRoomName.ToUpper()) && !string.IsNullOrWhiteSpace(currentRoomName))
                    {
                        newName = newName.ToUpper();
                    }

                    if (!newName.Equals(currentRoomName, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"🔄 [API] Updating default room name: '{currentRoomName}' -> '{newName}'");
                        using (var updateCmd = new SqlCommand("UPDATE ChatRooms SET RoomName = @RoomName WHERE RoomID = @RoomID", conn))
                        {
                            updateCmd.Parameters.AddWithValue("@RoomName", newName);
                            updateCmd.Parameters.AddWithValue("@RoomID", roomId);
                            await updateCmd.ExecuteNonQueryAsync();
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ UpdateGroupChatNameIfDefaultAsync error: {ex.Message}");
        }
    }

    public async Task<string> GetUserFullNameAsync(string userId, SqlConnection? existingConn = null)
    {
        try
        {
            var conn = existingConn;
            bool wasOpen = existingConn?.State == ConnectionState.Open;

            if (conn == null)
            {
                conn = CreateConnection("Chat"); // Use Chat connection which is reliable
                await conn.OpenAsync();
            }

            var query = @"SELECT LTRIM(RTRIM(ISNULL(pi.FirstName, '') + ' ' + ISNULL(pi.LastName, ''))) AS FullName
                FROM MISD.dbo.Users u
                INNER JOIN HRIS.dbo.PersonalInformation pi ON u.AISNo = pi.AISNo
                WHERE u.UserID = @UserID";
            
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@UserID", userId);
            var result = await cmd.ExecuteScalarAsync();

            if (!wasOpen && existingConn == null)
            {
                await conn.CloseAsync();
                conn.Dispose();
            }

            return result?.ToString()?.Trim() ?? userId;
        }
        catch
        {
            return userId;
        }
    }

    public async Task<List<(string UserID, string AISNo)>> GetRoomParticipantAISNosAsync(Guid roomId)
    {
        var list = new List<(string UserID, string AISNo)>();
        try
        {
            using var conn = CreateConnection("Chat");
            await conn.OpenAsync();
            var query = @"SELECT cp.UserID, ISNULL(u.AISNo, '') AS AISNo
                FROM ChatParticipants cp
                LEFT JOIN MISD.dbo.Users u ON cp.UserID = u.UserID
                WHERE cp.RoomID = @RoomID";
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@RoomID", roomId);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add((reader["UserID"].ToString() ?? "", reader["AISNo"].ToString() ?? ""));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [API] GetRoomParticipantAISNosAsync error: {ex.Message}");
        }
        return list;
    }

    public async Task<List<WorkStatusModel>> GetStatusListAsync()
    {
        var list = new List<WorkStatusModel>();
        try {
            using var conn = CreateConnection();
            await conn.OpenAsync();
            using var cmd = new SqlCommand("Lotsinventorynew.dbo.spREMS_ProjectProfile_Mobile", conn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@mode", "StatusList");
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new WorkStatusModel { 
                    StatusCode = reader["Code"] != DBNull.Value ? Convert.ToInt32(reader["Code"]) : 0, 
                    StatusText = reader["StatusName"]?.ToString() ?? "" 
                });
            }
        } catch (Exception ex) { Console.WriteLine($"❌ GetStatusList error: {ex.Message}"); }
        return list;
    }

    public async Task<List<ProjectEngineerModel>> GetAssignedEngineersAsync()
    {
        var list = new List<ProjectEngineerModel>();
        try {
            using var conn = CreateConnection();
            await conn.OpenAsync();
            using var cmd = new SqlCommand("Lotsinventorynew.dbo.spREMS_ProjectProfile_Mobile", conn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@mode", "EngineerListAssigned");
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new ProjectEngineerModel { 
                    Code = reader["Code"]?.ToString() ?? "", 
                    Name = reader["Name"]?.ToString() ?? "" 
                });
            }
        } catch (Exception ex) { Console.WriteLine($"❌ GetAssignedEngineers error: {ex.Message}"); }
        return list;
    }

    // ─── Project Profile Picture ───
    public async Task<(byte[]? fileBytes, string fileName, string contentType)> GetProjectProfilePicAsync(string controlNo)
    {
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();

            // Get StreamID from cover photo table
            string streamId = "";
            string photoName = "cover.jpg";
            string photoType = "image/jpeg";

            var query = "SELECT TOP 1 StreamID, ISNULL(FileName,'cover.jpg') as FileName, ISNULL(FileContentType,'image/jpeg') as FileContentType FROM Lotsinventorynew.dbo.trx_ProjectProfileCoverPhoto WHERE ControlNo = @ControlNo ORDER BY Id DESC";
            using (var cmd = new SqlCommand(query, conn))
            {
                cmd.CommandTimeout = 15;
                cmd.Parameters.AddWithValue("@ControlNo", controlNo);
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    streamId = reader["StreamID"]?.ToString() ?? "";
                    photoName = reader["FileName"]?.ToString() ?? "cover.jpg";
                    photoType = reader["FileContentType"]?.ToString() ?? "image/jpeg";
                }
            }

            if (string.IsNullOrEmpty(streamId))
                return (null, "", "");

            // Fetch the file from DMS via linked server
            var sqlFile = @"
                SELECT f.file_stream
                FROM OPENQUERY([system],
                    'SELECT stream_id, file_stream
                     FROM DMS.dbo.SGCFiles
                     WHERE stream_id = ''" + streamId.Replace("'", "''") + @"''') f";

            using (var cmd = new SqlCommand(sqlFile, conn))
            {
                cmd.CommandTimeout = 30;
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync() && reader[0] != DBNull.Value)
                {
                    return ((byte[])reader[0], photoName, photoType);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ GetProjectProfilePic error: {ex.Message}");
        }
        return (null, "", "");
    }

    /// <summary>
    /// Batch-checks which CtrlNos have cover photos and sets CoverPhotoUrl on matching projects.
    /// </summary>
    public async Task SetCoverPhotoUrlsAsync(List<ProjectModel> projects, string baseUrl)
    {
        if (projects == null || projects.Count == 0) return;
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();

            var query = "SELECT DISTINCT ControlNo FROM Lotsinventorynew.dbo.trx_ProjectProfileCoverPhoto WHERE ControlNo IN (" +
                string.Join(",", projects.Select((_, i) => $"@p{i}")) + ")";
            using var cmd = new SqlCommand(query, conn);
            cmd.CommandTimeout = 15;
            for (int i = 0; i < projects.Count; i++)
                cmd.Parameters.AddWithValue($"@p{i}", projects[i].CtrlNo);

            var ctrlNosWithPhoto = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                ctrlNosWithPhoto.Add(reader[0]?.ToString() ?? "");
            }

            foreach (var p in projects)
            {
                if (ctrlNosWithPhoto.Contains(p.CtrlNo))
                    p.CoverPhotoUrl = $"/api/Project/profile-pic/{p.CtrlNo}";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ SetCoverPhotoUrls error: {ex.Message}");
        }
    }

    public async Task<bool> SaveProjectProfilePicAsync(string controlNo, byte[] content, string fileName, string contentType, string auditUser)
    {
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();
            using var cmd = new SqlCommand("Lotsinventorynew.dbo.spREMS_ProjectProfile_Mobile", conn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@mode", "SaveProfilePic");
            cmd.Parameters.AddWithValue("@CtrlNo", controlNo);
            cmd.Parameters.AddWithValue("@filename", fileName);
            cmd.Parameters.AddWithValue("@contenttype", contentType);
            cmd.Parameters.AddWithValue("@filecontent", content);
            
            string effectiveAuditUser = _userContext.UserID ?? auditUser ?? "MobileUser";
            cmd.Parameters.AddWithValue("@AuditUser", effectiveAuditUser);
            cmd.Parameters.Add("@AuditDate", SqlDbType.DateTime).Value = DateTime.Now;
            
            await cmd.ExecuteNonQueryAsync();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ SaveProjectProfilePic error: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> SaveProjectEngineerAsync(string controlNo, string entityCode, bool isAssigned, bool isOIC, string auditUser)
    {
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();
            using var cmd = new SqlCommand("Lotsinventorynew.dbo.spREMS_ProjectProfile_Mobile", conn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@mode", "SaveEngineer");
            cmd.Parameters.AddWithValue("@CtrlNo", controlNo);
            cmd.Parameters.AddWithValue("@EntityCode", entityCode);
            cmd.Parameters.AddWithValue("@isAssigned", isAssigned ? 1 : 0);
            cmd.Parameters.AddWithValue("@isOIC", isOIC ? 1 : 0);
            
            string effectiveAuditUser = _userContext.UserID ?? auditUser ?? "MobileUser";
            cmd.Parameters.AddWithValue("@AuditUser", effectiveAuditUser);
            cmd.Parameters.Add("@AuditDate", SqlDbType.DateTime).Value = DateTime.Now;
            
            await cmd.ExecuteNonQueryAsync();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ SaveProjectEngineer error: {ex.Message}");
            return false;
        }
    }
    public async Task<(byte[]? fileBytes, string fileName, string contentType)> GetEngineersPicAsync(string controlNo, string entityCodes) { return (null, "", ""); }

    private int GetColumnIndex(SqlDataReader reader, string columnName)
    {
        try { return reader.GetOrdinal(columnName); } catch { return -1; }
    }
}

public static class SqlDataReaderExtensions
{
    public static bool HasColumn(this System.Data.IDataReader reader, string columnName)
    {
        for (int i = 0; i < reader.FieldCount; i++) if (reader.GetName(i).Equals(columnName, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
