using Microsoft.EntityFrameworkCore;
using Pix.Billing.Application;
using Pix.Billing.Domain;

namespace Pix.Billing.Infrastructure;

public class CobrancaJobRecord
{
    public string JobId { get; set; } = string.Empty;
    public string CobrancaId { get; set; } = string.Empty;
    public Guid PedidoId { get; set; }
    public decimal Valor { get; set; }
    public string Status { get; set; } = "PENDING";
    public DateTimeOffset CriadoEm { get; set; }
}

public class BillingOutboxRecord
{
    public string EventId { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset OccurredAt { get; set; }
}

public class BillingDbContext : DbContext
{
    public DbSet<CobrancaPix> Cobrancas { get; set; } = null!;
    public DbSet<CobrancaJobRecord> Jobs { get; set; } = null!;
    public DbSet<BillingOutboxRecord> Outbox { get; set; } = null!;

    public BillingDbContext(DbContextOptions<BillingDbContext> options) : base(options) { }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CobrancaPix>(entity =>
        {
            entity.ToTable("cobrancas_pix");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .HasConversion(v => v.Valor, v => new CobrancaId(v))
                .HasMaxLength(64);
            entity.Property(e => e.Valor).HasPrecision(18, 2);
            entity.Property(e => e.Versao).IsConcurrencyToken();
        });

        modelBuilder.Entity<CobrancaJobRecord>(entity =>
        {
            entity.ToTable("cobranca_jobs");
            entity.HasKey(e => e.JobId);
        });

        modelBuilder.Entity<BillingOutboxRecord>(entity =>
        {
            entity.ToTable("billing_outbox");
            entity.HasKey(e => e.EventId);
        });
    }
}

public class CobrancaJobStore : ICobrancaJobStore
{
    private readonly BillingDbContext _db;

    public CobrancaJobStore(BillingDbContext db)
    {
        _db = db;
    }

    public async Task<bool> CriarCobrancaComJobAsync(CobrancaPix cobranca, string jobId, CancellationToken ct = default)
    {
        if (await _db.Jobs.AnyAsync(j => j.JobId == jobId, ct) || await _db.Cobrancas.AnyAsync(c => c.Id == cobranca.Id, ct))
        {
            return false;
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            _db.Cobrancas.Add(cobranca);
            _db.Jobs.Add(new CobrancaJobRecord
            {
                JobId = jobId,
                CobrancaId = cobranca.Id.Valor,
                PedidoId = cobranca.PedidoId,
                Valor = cobranca.Valor,
                Status = "PENDING",
                CriadoEm = DateTimeOffset.UtcNow
            });

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            return false;
        }
    }

    public async Task CriarCobrancaEJobAsync(CobrancaPix cobranca, string jobId, CancellationToken ct = default)
    {
        await CriarCobrancaComJobAsync(cobranca, jobId, ct);
    }

    public async Task<CobrancaPix?> ObterPorIdAsync(CobrancaId id, CancellationToken ct = default)
    {
        return await _db.Cobrancas.FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task ConcluirJobComResultadoAsync(CobrancaPix cobranca, string jobId, string tipoEventoOutbox, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var job = await _db.Jobs.FirstOrDefaultAsync(j => j.JobId == jobId, ct);
            if (job != null)
            {
                job.Status = "COMPLETED";
            }

            _db.Outbox.Add(new BillingOutboxRecord
            {
                EventId = Guid.NewGuid().ToString(),
                Tipo = tipoEventoOutbox,
                PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    cobrancaId = cobranca.Id.Valor,
                    pedidoId = cobranca.PedidoId,
                    estado = cobranca.Estado.ToString(),
                    artefatoQr = cobranca.ArtefatoQr,
                    motivoRecusa = cobranca.MotivoRecusa
                }),
                OccurredAt = DateTimeOffset.UtcNow
            });

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
