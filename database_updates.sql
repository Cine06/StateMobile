USE [Chat]
GO

CREATE OR ALTER PROCEDURE [dbo].[spChat_MarkAsRead]
    @RoomID UNIQUEIDENTIFIER,
    @UserID VARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    -- I-update ang lahat ng messages sa room na hindi ang user ang sender
    UPDATE [dbo].[Messages_Data]
    SET IsRead = 1
    WHERE RoomID = @RoomID 
      AND SenderID <> @UserID 
      AND IsRead = 0;

    -- Opsyonal: Mag-insert din sa MessageReceipts kung kailangan ng detailed tracking
    INSERT INTO [dbo].[MessageReceipts] (MessageID, UserID, ReadAt)
    SELECT MessageID, @UserID, GETDATE()
    FROM [dbo].[Messages_Data] m
    WHERE RoomID = @RoomID 
      AND SenderID <> @UserID
      AND NOT EXISTS (SELECT 1 FROM [dbo].[MessageReceipts] mr WHERE mr.MessageID = m.MessageID AND mr.UserID = @UserID);
END
GO