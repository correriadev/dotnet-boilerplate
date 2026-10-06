-- Script 006: Criar tabela de saldos no schema [pix] para réplica de leitura
IF NOT EXISTS (SELECT * FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'pix' AND t.name = 'Saldos')
BEGIN
    CREATE TABLE [pix].[Saldos] (
        [ContaId] VARCHAR(50) NOT NULL PRIMARY KEY,
        [SaldoDisponivel] DECIMAL(18, 2) NOT NULL,
        [LimiteDiario] DECIMAL(18, 2) NOT NULL,
        [Moeda] VARCHAR(3) NOT NULL DEFAULT 'BRL',
        [AtualizadoEm] DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
    );

    IF NOT EXISTS (SELECT 1 FROM [pix].[Saldos] WHERE [ContaId] = 'conta-spb-001')
    BEGIN
        INSERT INTO [pix].[Saldos] ([ContaId], [SaldoDisponivel], [LimiteDiario], [Moeda], [AtualizadoEm])
        VALUES ('conta-spb-001', 15000.00, 50000.00, 'BRL', SYSDATETIMEOFFSET());
    END;

    IF NOT EXISTS (SELECT 1 FROM [pix].[Saldos] WHERE [ContaId] = 'cnt-cache-fallback-1')
    BEGIN
        INSERT INTO [pix].[Saldos] ([ContaId], [SaldoDisponivel], [LimiteDiario], [Moeda], [AtualizadoEm])
        VALUES ('cnt-cache-fallback-1', 5000.00, 10000.00, 'BRL', SYSDATETIMEOFFSET());
    END;
END;
