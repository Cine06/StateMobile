USE [Chat]
GO

-- 1. Dagdag columns sa Messages_Data
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Messages_Data') AND name = 'AttachmentPath')
BEGIN
    ALTER TABLE Messages_Data ADD AttachmentPath NVARCHAR(MAX) NULL;
    ALTER TABLE Messages_Data ADD AttachmentType NVARCHAR(50) NULL; -- 'image', 'pdf'
    ALTER TABLE Messages_Data ADD IsDeletedForEveryone BIT DEFAULT 0;
END
GO

-- 2. I-update ang Messages View para kasama ang bagong columns
CREATE OR ALTER VIEW [dbo].[Messages]
AS
SELECT
    [MessageID],
    [RoomID],
    [SenderID],
    CASE 
        WHEN IsDeletedForEveryone = 1 THEN 'This message was deleted.'
        ELSE CAST(DECRYPTBYPASSPHRASE('MySecretKey2026', [EncryptedText]) AS NVARCHAR(MAX)) 
    END AS [MessageText],
    [Timestamp],
    [IsRead],
    [AttachmentPath],
    [AttachmentType],
    [IsDeletedForEveryone]
FROM [dbo].[Messages_Data];
GO

-- 3. I-update ang spChat_GetMessages para makuha ang attachments
CREATE OR ALTER PROCEDURE [dbo].[spChat_GetMessages]
    @RoomID UNIQUEIDENTIFIER,
    @UserID VARCHAR(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @LastDeletedAt DATETIME = '1900-01-01';
    
    IF @UserID IS NOT NULL
    BEGIN
        SELECT @LastDeletedAt = ISNULL(LastDeletedAt, '1900-01-01')
        FROM [dbo].[ChatParticipants]
        WHERE RoomID = @RoomID AND UserID = @UserID;
    END

    SELECT 
        m.MessageID, 
        m.RoomID, 
        m.SenderID, 
        CASE 
            WHEN m.IsDeletedForEveryone = 1 THEN 'This message was deleted.'
            ELSE CAST(DECRYPTBYPASSPHRASE('MySecretKey2026', m.EncryptedText) AS NVARCHAR(MAX)) 
        END AS MessageText, 
        m.Timestamp, 
        m.IsRead,
        m.AttachmentPath,
        m.AttachmentType,
        m.IsDeletedForEveryone
    FROM [dbo].[Messages_Data] m
    LEFT JOIN [dbo].[MessageDeletions] md ON m.MessageID = md.MessageID AND md.UserID = @UserID
    WHERE m.RoomID = @RoomID 
      AND m.Timestamp > @LastDeletedAt 
      AND md.MessageID IS NULL
    ORDER BY m.Timestamp ASC;
END
GO

-- 4. Bagong Procedure para sa Professional Deletion
CREATE OR ALTER PROCEDURE [dbo].[spChat_DeleteMessage]
    @MessageID BIGINT,
    @UserID VARCHAR(50),
    @ForEveryone BIT
AS
BEGIN
    SET NOCOUNT ON;

    IF @ForEveryone = 1
    BEGIN
        -- Siguraduhin na ang nag-delete ay ang sender
        UPDATE Messages_Data 
        SET IsDeletedForEveryone = 1, 
            EncryptedText = NULL, -- Burahin ang content para sa security
            AttachmentPath = NULL
        WHERE MessageID = @MessageID AND SenderID = @UserID;
    END
    ELSE
    BEGIN
        -- Delete for me lang (MessageDeletions table)
        IF NOT EXISTS (SELECT 1 FROM MessageDeletions WHERE MessageID = @MessageID AND UserID = @UserID)
        BEGIN
            INSERT INTO MessageDeletions (MessageID, UserID, DeletedAt)
            VALUES (@MessageID, @UserID, GETDATE());
        END
    END
END
GO