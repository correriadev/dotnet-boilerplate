-- Script 007: Criar índice filtrado para mensagens pendentes e otimização de varredura
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_OutboxRecords_Pending' AND object_id = OBJECT_ID('pix.OutboxRecords'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_OutboxRecords_Pending
    ON [pix].[OutboxRecords] ([CriadoEm] ASC)
    INCLUDE ([EventId], [Tipo], [CorrelationId], [Status])
    WHERE [Status] = 'PENDING';
END;
