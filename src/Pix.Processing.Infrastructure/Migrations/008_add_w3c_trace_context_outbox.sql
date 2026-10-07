IF NOT EXISTS (
    SELECT 1 FROM sys.columns 
    WHERE object_id = OBJECT_ID('[pix].[OutboxRecords]') 
    AND name = 'TraceParent'
)
BEGIN
    ALTER TABLE [pix].[OutboxRecords] ADD [TraceParent] NVARCHAR(100) NULL;
END

IF NOT EXISTS (
    SELECT 1 FROM sys.columns 
    WHERE object_id = OBJECT_ID('[pix].[OutboxRecords]') 
    AND name = 'TraceState'
)
BEGIN
    ALTER TABLE [pix].[OutboxRecords] ADD [TraceState] NVARCHAR(500) NULL;
END
