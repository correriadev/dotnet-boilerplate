IF NOT EXISTS (SELECT * FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'pix' AND t.name = 'IdempotencyRecords')
BEGIN
    CREATE TABLE [pix].[IdempotencyRecords] (
        [ChaveComposta] NVARCHAR(250) NOT NULL,
        [ClienteId] NVARCHAR(100) NOT NULL,
        [TipoOperacao] NVARCHAR(50) NOT NULL,
        [Chave] NVARCHAR(100) NOT NULL,
        [FingerprintHash] NVARCHAR(128) NOT NULL,
        [FingerprintVersao] NVARCHAR(20) NOT NULL,
        [PedidoId] UNIQUEIDENTIFIER NOT NULL,
        [StatusCode] INT NOT NULL,
        [HeadersJson] NVARCHAR(MAX) NOT NULL,
        [CorpoJson] NVARCHAR(MAX) NOT NULL,
        [CriadoEm] DATETIMEOFFSET NOT NULL,
        CONSTRAINT [PK_IdempotencyRecords] PRIMARY KEY CLUSTERED ([ChaveComposta])
    );
END
