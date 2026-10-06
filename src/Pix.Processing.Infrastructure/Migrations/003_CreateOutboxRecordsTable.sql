IF NOT EXISTS (SELECT * FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'pix' AND t.name = 'OutboxRecords')
BEGIN
    CREATE TABLE [pix].[OutboxRecords] (
        [EventId] NVARCHAR(100) NOT NULL,
        [Tipo] NVARCHAR(150) NOT NULL,
        [SchemaVersion] NVARCHAR(20) NOT NULL,
        [CorrelationId] NVARCHAR(100) NOT NULL,
        [PayloadJson] NVARCHAR(MAX) NOT NULL,
        [Status] NVARCHAR(50) NOT NULL DEFAULT 'PENDING',
        [OccurredAt] DATETIMEOFFSET NOT NULL,
        [CriadoEm] DATETIMEOFFSET NOT NULL,
        [PublishedAt] DATETIMEOFFSET NULL,
        [LockOwner] NVARCHAR(100) NULL,
        [LockedUntil] DATETIMEOFFSET NULL,
        CONSTRAINT [PK_OutboxRecords] PRIMARY KEY CLUSTERED ([EventId])
    );

    CREATE NONCLUSTERED INDEX [IX_OutboxRecords_Status_CriadoEm] 
    ON [pix].[OutboxRecords] ([Status], [CriadoEm]);
END
