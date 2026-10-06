IF NOT EXISTS (SELECT * FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'pix' AND t.name = 'ProcessedIds')
BEGIN
    CREATE TABLE [pix].[ProcessedIds] (
        [EndToEndId] NVARCHAR(32) NOT NULL,
        [PedidoId] UNIQUEIDENTIFIER NOT NULL,
        [Status] NVARCHAR(50) NOT NULL,
        [ExecutadoEm] DATETIMEOFFSET NOT NULL,
        CONSTRAINT [PK_ProcessedIds] PRIMARY KEY CLUSTERED ([EndToEndId])
    );
END
