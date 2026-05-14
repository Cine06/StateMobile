-- 1. DOCUMENT SCANNING & SIGNATURES
-- Header for a scanning session
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ScannedDocuments')
BEGIN
    CREATE TABLE ScannedDocuments (
        Id INT PRIMARY KEY IDENTITY(1,1),
        UserId INT NOT NULL,
        Title NVARCHAR(255) NOT NULL,
        CreatedAt DATETIME DEFAULT GETDATE(),
        IsFinalized BIT DEFAULT 0 -- Set to 1 when PDF is generated
    );
END

-- Individual pages within a document
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ScannedPages')
BEGIN
    CREATE TABLE ScannedPages (
        Id INT PRIMARY KEY IDENTITY(1,1),
        DocumentId INT NOT NULL FOREIGN KEY REFERENCES ScannedDocuments(Id),
        ImagePath NVARCHAR(MAX) NOT NULL, -- Path to storage or Base64
        PageOrder INT NOT NULL,
        CreatedAt DATETIME DEFAULT GETDATE()
    );
END

-- Signatures placed on specific pages
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PageSignatures')
BEGIN
    CREATE TABLE PageSignatures (
        Id INT PRIMARY KEY IDENTITY(1,1),
        PageId INT NOT NULL FOREIGN KEY REFERENCES ScannedPages(Id),
        SignatureData VARBINARY(MAX) NOT NULL, -- The actual signature image
        CoordinateX FLOAT NOT NULL,
        CoordinateY FLOAT NOT NULL,
        ScaleWidth FLOAT DEFAULT 140,
        ScaleHeight FLOAT DEFAULT 70,
        CreatedAt DATETIME DEFAULT GETDATE()
    );
END

-- 2. PROFESSIONAL MESSENGER SYSTEM
-- Chat Room metadata
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ChatRooms')
BEGIN
    CREATE TABLE ChatRooms (
        Id INT PRIMARY KEY IDENTITY(1,1),
        RoomName NVARCHAR(100), -- Null for 1-on-1 (use other user's name)
        RoomImage NVARCHAR(MAX), -- URL or Base64 for group icon
        IsGroup BIT DEFAULT 0,
        CreatedAt DATETIME DEFAULT GETDATE()
    );
END

-- Mapping users to rooms
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ChatParticipants')
BEGIN
    CREATE TABLE ChatParticipants (
        RoomId INT NOT NULL FOREIGN KEY REFERENCES ChatRooms(Id),
        UserId INT NOT NULL, -- Foreign key to your existing Users table
        JoinedAt DATETIME DEFAULT GETDATE(),
        PRIMARY KEY (RoomId, UserId)
    );
END

-- Message history
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ChatMessages')
BEGIN
    CREATE TABLE ChatMessages (
        Id INT PRIMARY KEY IDENTITY(1,1),
        RoomId INT NOT NULL FOREIGN KEY REFERENCES ChatRooms(Id),
        SenderId INT NOT NULL,
        Content NVARCHAR(MAX) NOT NULL,
        Timestamp DATETIME DEFAULT GETDATE(),
        IsRead BIT DEFAULT 0 -- Used for unread badges in UI
    );
END

-- 3. USER STATUS UPDATES
-- Adding status tracking to existing User table
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Users') AND name = 'OnlineStatus')
BEGIN
    ALTER TABLE Users ADD OnlineStatus NVARCHAR(20) DEFAULT 'Offline';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Users') AND name = 'LastSeen')
BEGIN
    ALTER TABLE Users ADD LastSeen DATETIME;
END