USE [Chat]
GO

/* 1. UPDATE TABLE: Messages_Data */
-- Nagdadagdag ng columns para sa Attachments at Deletion Status
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Messages_Data') AND name = 'AttachmentPath')
BEGIN
    ALTER TABLE Messages_Data ADD AttachmentPath NVARCHAR(MAX) NULL;
    ALTER TABLE Messages_Data ADD AttachmentType NVARCHAR(50) NULL; -- 'image', 'pdf', 'voice'
    ALTER TABLE Messages_Data ADD IsDeletedForEveryone BIT CONSTRAINT DF_Messages_Data_IsDeletedForEveryone DEFAULT 0 NOT NULL;
END
GO

/* 2. UPDATE VIEW: Messages */
-- Ito ang ginagamit ng API para mag-fetch at mag-insert. 
-- Nilagyan natin ng logic para itago ang text kung 'Deleted for Everyone' na ito.
CREATE OR ALTER VIEW [dbo].[Messages]
AS
SELECT
    [MessageID],
    [RoomID],
    [SenderID],
    CASE 
        WHEN IsDeletedForEveryone = 1 THEN '🚫 This message was deleted.'
        ELSE CAST(DECRYPTBYPASSPHRASE('MySecretKey2026', [EncryptedText]) AS NVARCHAR(MAX)) 
    END AS [MessageText],
    [Timestamp],
    [IsRead],
    [AttachmentPath],
    [AttachmentType],
    [IsDeletedForEveryone]
FROM [dbo].[Messages_Data];
GO

/* 3. UPDATE TRIGGER: trg_Messages_Insert */
-- Kailangan nating i-update ang trigger para tanggapin ang AttachmentPath at Type kapag nag-insert ang API.
CREATE OR ALTER TRIGGER [dbo].[trg_Messages_Insert]
ON [dbo].[Messages]
INSTEAD OF INSERT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO [dbo].[Messages_Data]
    (
        [RoomID],
        [SenderID],
        [EncryptedText],
        [Timestamp],
        [IsRead],
        [AttachmentPath],
        [AttachmentType]
    )
    SELECT
        i.[RoomID],
        i.[SenderID],
        ENCRYPTBYPASSPHRASE('MySecretKey2026', i.[MessageText]),
        ISNULL(i.[Timestamp], GETDATE()),
        ISNULL(i.[IsRead], 0),
        i.[AttachmentPath],
        i.[AttachmentType]
    FROM inserted i;
END;
GO

/* 4. UPDATE STORED PROCEDURE: spChat_GetMessages */
-- Sinisiguro nito na makukuha ng Mobile App ang attachment info at deletion status.
CREATE OR ALTER PROCEDURE [dbo].[spChat_GetMessages]
    @RoomID UNIQUEIDENTIFIER,
    @UserID VARCHAR(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @LastDeletedAt DATETIME = '1900-01-01';
    
    -- Kunin ang huling timestamp kung kailan "Clear Chat" ang user
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
            WHEN m.IsDeletedForEveryone = 1 THEN '🚫 This message was deleted.'
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
      AND md.MessageID IS NULL -- Huwag ipakita kung "Delete for Me"
    ORDER BY m.Timestamp ASC;
END
GO

/* 5. NEW STORED PROCEDURE: spChat_DeleteMessage */
-- Logic para sa "Delete for Me" vs "Delete for Everyone"
CREATE OR ALTER PROCEDURE [dbo].[spChat_DeleteMessage]
    @MessageID BIGINT,
    @UserID VARCHAR(50),
    @ForEveryone BIT
AS
BEGIN
    SET NOCOUNT ON;

    IF @ForEveryone = 1
    BEGIN
        -- Delete for Everyone: I-update ang main table (Dapat sender ang nag-delete)
        UPDATE Messages_Data 
        SET IsDeletedForEveryone = 1, 
            EncryptedText = NULL, -- Burahin ang content para sa privacy
            AttachmentPath = NULL
        WHERE MessageID = @MessageID AND SenderID = @UserID;
    END
    ELSE
    BEGIN
        -- Delete for Me: Mag-insert sa MessageDeletions table
        IF NOT EXISTS (SELECT 1 FROM MessageDeletions WHERE MessageID = @MessageID AND UserID = @UserID)
        BEGIN
            INSERT INTO MessageDeletions (MessageID, UserID, DeletedAt)
            VALUES (@MessageID, @UserID, GETDATE());
        END
    END
END
GO