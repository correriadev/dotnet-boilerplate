using FluentAssertions;
using System.Reflection;
using Xunit;

namespace Integration;

public class DbUpScriptTests
{
    [Fact]
    public void DBUP_011_Should_Conter_Scripts_DDL_Do_Schema_Pix_E_Indices_Obrigatorios()
    {
        // Given: Assembly de Infrastructure onde os scripts SQL do DbUp devem residir
        var infraAssembly = typeof(Pix.Processing.Infrastructure.PixDbContext).Assembly;

        // When: Busca os scripts SQL embutidos ou recursos na pasta Migrations
        var resourceNames = infraAssembly.GetManifestResourceNames()
            .Where(r => r.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Then 1: Deve conter scripts SQL de migração
        resourceNames.Should().NotBeEmpty("Scripts SQL do DbUp devem estar embutidos como EmbeddedResource no Pix.Processing.Infrastructure");

        // Consolida o conteúdo de todos os scripts
        var todosScriptsSql = string.Join("\n", resourceNames.Select(r =>
        {
            using var stream = infraAssembly.GetManifestResourceStream(r);
            using var reader = new System.IO.StreamReader(stream!);
            return reader.ReadToEnd();
        }));

        // Then 2: Deve conter a criação do schema [pix]
        todosScriptsSql.Should().Contain("CREATE SCHEMA [pix]", "Os scripts devem criar o schema dedicado [pix]");

        // Then 3: Deve conter as 4 tabelas obrigatórias no schema [pix]
        todosScriptsSql.Should().Contain("[pix].[Pedidos]", "Deve conter a tabela de pedidos no schema pix");
        todosScriptsSql.Should().Contain("[pix].[OutboxRecords]", "Deve conter a tabela de Outbox no schema pix");
        todosScriptsSql.Should().Contain("[pix].[IdempotencyRecords]", "Deve conter a tabela de Idempotência no schema pix");
        todosScriptsSql.Should().Contain("[pix].[ProcessedIds]", "Deve conter a tabela de IDs processados no schema pix");

        // Then 4: Tabela Outbox deve conter índice para Status e CriadoEm
        todosScriptsSql.Should().Contain("IX_OutboxRecords_Status_CriadoEm", "Deve conter índice composto para polling eficiente do Outbox");

        // Then 5: ProcessedIds deve ter chave primária em EndToEndId
        todosScriptsSql.Should().Contain("PRIMARY KEY CLUSTERED ([EndToEndId])", "ProcessedIds deve ter restrição UNIQUE/PK em EndToEndId");
    }

    [Fact]
    public void DBUP_011_Should_Descobrir_Todos_Scripts_Pelo_DbUpRunner()
    {
        // Given: Conexão para configurar a engine do DbUp
        var fakeConnString = "Server=localhost;Database=FakeDb;User Id=sa;Password=Password123!;TrustServerCertificate=True;";

        // When: Cria a engine do DbUp
        var engine = Pix.Processing.Infrastructure.DbUpRunner.CriarEngine(fakeConnString);
        var scripts = engine.GetDiscoveredScripts();

        // Then: Deve descobrir os 5 scripts de migração do schema pix
        scripts.Should().HaveCountGreaterOrEqualTo(5);
        scripts.Select(s => s.Name).Should().Contain(name => name.Contains("001_CreateSchemaPix.sql"));
        scripts.Select(s => s.Name).Should().Contain(name => name.Contains("002_CreatePedidosTable.sql"));
        scripts.Select(s => s.Name).Should().Contain(name => name.Contains("003_CreateOutboxRecordsTable.sql"));
        scripts.Select(s => s.Name).Should().Contain(name => name.Contains("004_CreateIdempotencyRecordsTable.sql"));
        scripts.Select(s => s.Name).Should().Contain(name => name.Contains("005_CreateProcessedIdsTable.sql"));
    }
}

