IF NOT EXISTS (SELECT * FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'pix' AND t.name = 'Pedidos')
BEGIN
    CREATE TABLE [pix].[Pedidos] (
        [Id] UNIQUEIDENTIFIER NOT NULL,
        [ClienteId] NVARCHAR(100) NOT NULL,
        [ContaId] NVARCHAR(100) NOT NULL,
        [TipoOperacao] NVARCHAR(50) NOT NULL,
        [EndToEndId] NVARCHAR(32) NULL,
        [Valor] DECIMAL(18, 2) NOT NULL,
        [Moeda] NVARCHAR(3) NOT NULL DEFAULT 'BRL',
        [Estado] NVARCHAR(50) NOT NULL,
        [Versao] INT NOT NULL DEFAULT 1,
        [CriadoEm] DATETIMEOFFSET NOT NULL,
        [AtualizadoEm] DATETIMEOFFSET NULL,
        CONSTRAINT [PK_Pedidos] PRIMARY KEY CLUSTERED ([Id])
    );
END
