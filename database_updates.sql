USE [Chat]
GO

-- Dagdag columns para sa Attachments at Deletion
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Messages_Data') AND name = 'AttachmentPath')
BEGIN
    ALTER TABLE Messages_Data ADD AttachmentPath NVARCHAR(MAX) NULL;
    ALTER TABLE Messages_Data ADD AttachmentType NVARCHAR(50) NULL; -- 'image', 'pdf', 'doc'
    ALTER TABLE Messages_Data ADD IsDeletedForEveryone BIT DEFAULT 0;
END
GO

-- Stored Procedure para sa Delete for Everyone
CREATE OR ALTER PROCEDURE [dbo].[spChat_DeleteMessage]
    @MessageID BIGINT,
    @UserID VARCHAR(50),
    @ForEveryone BIT
AS
BEGIN
    IF @ForEveryone = 1
    BEGIN
        -- Check kung ang nag-delete ay ang sender
        UPDATE Messages_Data 
        SET IsDeletedForEveryone = 1, EncryptedText = NULL 
        WHERE MessageID = @MessageID AND SenderID = @UserID;
    END
    ELSE
    BEGIN
        -- Delete for me lang
        IF NOT EXISTS (SELECT 1 FROM MessageDeletions WHERE MessageID = @MessageID AND UserID = @UserID)
        BEGIN
            INSERT INTO MessageDeletions (MessageID, UserID, DeletedAt)
            VALUES (@MessageID, @UserID, GETDATE());
        END
    END
END
GO