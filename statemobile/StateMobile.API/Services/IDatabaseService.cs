using StateMobile.API.Models;

namespace StateMobile.API.Services;

public interface IDatabaseService
{
    Task<List<ProjectModel>> GetProjectsAsync();
    Task<ProjectModel?> GetProjectByControlNoAsync(string controlNo);
    Task<List<WorkStatusModel>> GetStatusListAsync();
    Task<List<HouseModelFilterModel>> GetHouseModelListAsync();
    Task<List<ProjectEngineerModel>> GetAssignedEngineersAsync();
    Task<List<ProjectModel>> GetFilteredProjectsAsync(string? statusCodes, string? engineerCodes, string? modelCodes, string? sortBy, string? sortDir);
    Task<List<NotificationModel>> GetNotificationsAsync(string aisno);
    Task<object> GetUnreadCountsAsync(string aisno);
    Task<bool> MarkNotificationAsReadAsync(long code);
    Task<bool> DeleteNotificationAsync(long code);
    Task<bool> ArchiveNotificationAsync(long code);
    Task<bool> MarkChatMessagesAsReadAsync(Guid roomId, string userId);
    Task<List<ChatRoomModel>> GetUserChatRoomsAsync(string userId);
    Task<ChatRoomModel?> GetChatRoomAsync(Guid roomId, string? userId = null);
    Task<List<ChatMessageModel>> GetChatMessagesAsync(Guid roomId, string? userId = null);
    Task<ChatMessagesPageResult> GetChatMessagesPageAsync(Guid roomId, string? userId = null, long? beforeMessageId = null, DateTime? beforeTimestamp = null, int pageSize = 50);
    Task<ChatMessageContentResponse?> GetChatMessageContentAsync(long messageId);
    Task<long> SendChatMessageAsync(Guid roomId, string senderId, string messageText);
    Task<ChatRoomModel?> GetOrCreateChatRoomAsync(string currentUserId, string targetUserId, string targetFullName);
    Task<ChatRoomModel?> CreateGroupChatRoomAsync(string currentUserId, List<string> targetUserIds, string roomName);
    Task<List<User>> SearchUsersAsync(string searchTerm, Guid? excludeRoomId = null);
    Task<bool> DeleteChatRoomAsync(Guid roomId, string userId);
    Task<bool> SendNotificationAsync(NotificationModel notification);
    Task<UserStatusModel?> GetUserStatusAsync(string userId);
    Task<bool> UpdateUserStatusAsync(string userId, bool isOnline, string? connectionId = null);
    Task<bool> AddReactionAsync(long messageId, string userId, string reactionType);
    Task<bool> RemoveReactionAsync(long messageId, string userId);
    Task<bool> MarkMessageAsReadAsync(long messageId, string userId);
    Task<(bool success, Guid roomId)> DeleteMessageAsync(long messageId, string userId, bool forEveryone);
    Task<(bool success, Guid roomId)> DeleteMessagesAsync(List<long> messageIds, string userId, bool forEveryone);
    Task<(bool success, string systemText, long messageId)> UpdateChatRoomAsync(Guid roomId, string roomName, string roomPhoto, string? actorId = null);
    Task<List<ChatParticipantModel>> GetChatParticipantsAsync(Guid roomId);

    Task<(bool success, string systemText, long messageId)> UpdateParticipantRoleAsync(Guid roomId, string userId, bool isAdmin, string? actorId = null);
    Task<List<(string targetName, long messageId)>> AddParticipantsToRoomAsync(Guid roomId, List<string> userIds, string addedByUserId);
    Task<(string systemText, long messageId)> RemoveParticipantAsync(Guid roomId, string userId, string? actorId = null);
    Task<string> GetUserFullNameAsync(string userId, Microsoft.Data.SqlClient.SqlConnection? existingConn = null);
    Task<List<(string UserID, string AISNo)>> GetRoomParticipantAISNosAsync(Guid roomId);
    
    // ─── Project Profile Diary (Updates) ───
    Task<List<ProjectDiaryModel>> GetProjectDiaryAsync(string controlNo, string startDate = "", string endDate = "", string auditUser = "");
    Task<int> SaveProjectDiaryAsync(SaveDiaryRequest request);
    Task<int> UpdateProjectDiaryAsync(SaveDiaryRequest request);
    Task<bool> DeleteProjectDiaryAsync(int diaryEntryId);
    Task<List<ProjectDiaryFileModel>> GetProjectDiaryFilesAsync(string controlNo, string startDate = "", string endDate = "", string auditUser = "");
    Task<bool> SaveProjectDiaryFileAsync(SaveDiaryFileRequest request);
    Task<bool> DeleteProjectDiaryFileAsync(int fileId);
    Task<(byte[]? fileBytes, string fileName, string contentType)> GetDiaryFileContentAsync(string streamId);
    
    // ─── Project Profile Picture ───
    Task<(byte[]? fileBytes, string fileName, string contentType)> GetProjectProfilePicAsync(string controlNo);
    Task<bool> SaveProjectProfilePicAsync(string controlNo, byte[] content, string fileName, string contentType, string auditUser);
    Task<(byte[]? fileBytes, string fileName, string contentType)> GetEngineersPicAsync(string controlNo, string entityCodes);
    Task<bool> SaveProjectEngineerAsync(string controlNo, string entityCode, bool isAssigned, bool isOIC, string auditUser);
}
