using Microsoft.EntityFrameworkCore;
using Pix.Processing.Domain.Entities;
using Pix.Processing.Domain.Enums;
using Pix.Processing.Domain.ValueObjects;
using Pix.Processing.Infrastructure.Entities;

namespace Pix.Processing.Infrastructure;

public class PixDbContext : DbContext
{
    public DbSet<PedidoPix> Pedidos { get; set; } = null!;
    public DbSet<CoordenacaoConta> CoordenacoesConta { get; set; } = null!;
    public DbSet<RegistroExecucao> ProcessedIds { get; set; } = null!;
    public DbSet<IdempotencyRecord> IdempotencyRecords { get; set; } = null!;
    public DbSet<OutboxRecord> OutboxRecords { get; set; } = null!;
    public DbSet<InboxRecord> InboxRecords { get; set; } = null!;
    public DbSet<PendingCommandRecord> PendingCommands { get; set; } = null!;

    public PixDbContext(DbContextOptions<PixDbContext> options) : base(options) { }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // PedidoPix
        modelBuilder.Entity<PedidoPix>(entity =>
        {
            entity.ToTable("pedidos_pix");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ClienteId)
                .HasConversion(v => v.Valor, v => new ClienteId(v))
                .HasMaxLength(64)
                .IsRequired();
            entity.Property(e => e.ContaId)
                .HasConversion(v => v.Valor, v => new ContaId(v))
                .HasMaxLength(64)
                .IsRequired();
            entity.Property(e => e.EndToEndId)
                .HasConversion(v => v.HasValue ? v.Value.Valor : null, v => v != null ? new EndToEndId(v) : null)
                .HasMaxLength(64);
            entity.OwnsOne(e => e.Valor, v =>
            {
                v.Property(m => m.Quantia).HasColumnName("valor_quantia").HasPrecision(18, 2);
                v.Property(m => m.Moeda).HasColumnName("valor_moeda").HasMaxLength(3);
            });
            entity.OwnsOne(e => e.UltimaEvidencia, ev =>
            {
                ev.Property(x => x.Operacao).HasColumnName("evidencia_operacao").HasMaxLength(64);
                ev.Property(x => x.Tipo).HasColumnName("evidencia_tipo").HasMaxLength(64);
                ev.Property(x => x.Fonte).HasColumnName("evidencia_fonte").HasMaxLength(64);
                ev.Property(x => x.Referencia).HasColumnName("evidencia_referencia").HasMaxLength(128);
                ev.Property(x => x.ObservadoEm).HasColumnName("evidencia_observado_em");
                ev.Property(x => x.Sucesso).HasColumnName("evidencia_sucesso");
            });
            entity.Property(e => e.Versao).IsConcurrencyToken();
        });

        // CoordenacaoConta
        modelBuilder.Entity<CoordenacaoConta>(entity =>
        {
            entity.ToTable("coordenacoes_conta");
            entity.HasKey(e => e.ContaId);
            entity.Property(e => e.ContaId)
                .HasConversion(v => v.Valor, v => new ContaId(v))
                .HasMaxLength(64);
            entity.Property(e => e.Versao).IsConcurrencyToken();
        });

        // RegistroExecucao (processed_ids)
        modelBuilder.Entity<RegistroExecucao>(entity =>
        {
            entity.ToTable("processed_ids");
            entity.HasKey(e => e.EndToEndId);
            entity.Property(e => e.EndToEndId)
                .HasConversion(v => v.Valor, v => new EndToEndId(v))
                .HasMaxLength(64);
            entity.Property(e => e.ContaId)
                .HasConversion(v => v.Valor, v => new ContaId(v))
                .HasMaxLength(64);
            entity.OwnsOne(e => e.EvidenciaResultado, ev =>
            {
                ev.Property(x => x.Operacao).HasColumnName("evidencia_operacao").HasMaxLength(64);
                ev.Property(x => x.Tipo).HasColumnName("evidencia_tipo").HasMaxLength(64);
                ev.Property(x => x.Fonte).HasColumnName("evidencia_fonte").HasMaxLength(64);
                ev.Property(x => x.Referencia).HasColumnName("evidencia_referencia").HasMaxLength(128);
                ev.Property(x => x.ObservadoEm).HasColumnName("evidencia_observado_em");
                ev.Property(x => x.Sucesso).HasColumnName("evidencia_sucesso");
            });
            entity.Property(e => e.Version).IsConcurrencyToken();
        });

        // IdempotencyRecord
        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.ToTable("idempotency_records");
            entity.HasKey(e => e.ChaveComposta);
            entity.HasIndex(e => new { e.ClienteId, e.TipoOperacao, e.Chave }).IsUnique();
        });

        // OutboxRecord
        modelBuilder.Entity<OutboxRecord>(entity =>
        {
            entity.ToTable("OutboxRecords", "pix");
            entity.HasKey(e => e.EventId);
            entity.HasIndex(e => e.Status);
        });

        // InboxRecord
        modelBuilder.Entity<InboxRecord>(entity =>
        {
            entity.ToTable("inbox_records");
            entity.HasKey(e => e.EventId);
        });

        // PendingCommandRecord
        modelBuilder.Entity<PendingCommandRecord>(entity =>
        {
            entity.ToTable("pending_commands");
            entity.HasKey(e => e.EventId);
            entity.HasIndex(e => new { e.ContaId, e.Sequencia });
        });
    }
}
