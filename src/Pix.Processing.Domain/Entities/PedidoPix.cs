using Pix.Processing.Domain.Enums;
using Pix.Processing.Domain.ValueObjects;

namespace Pix.Processing.Domain.Entities;

public class PedidoPix
{
    public Guid Id { get; private set; }
    public ClienteId ClienteId { get; private set; }
    public ContaId ContaId { get; private set; }
    public TipoOperacaoPix TipoOperacao { get; private set; }
    public EndToEndId? EndToEndId { get; private set; }
    public Money Valor { get; private set; }
    public EstadoPedidoPix Estado { get; private set; }
    public int Versao { get; private set; }
    public long Generation { get; private set; }
    public long? SequenciaConta { get; private set; }
    public string? OwnerVigente { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public bool IntencaoEnvioRegistrada { get; private set; }
    public ExternalEvidence? UltimaEvidencia { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }
    public DateTimeOffset AtualizadoEm { get; private set; }

    // Construtor EF
    private PedidoPix() { Valor = null!; }

    public PedidoPix(
        Guid id,
        ClienteId clienteId,
        ContaId contaId,
        TipoOperacaoPix tipoOperacao,
        EndToEndId? endToEndId,
        Money valor,
        long? sequenciaConta = null,
        DateTimeOffset? criadoEm = null)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        ClienteId = clienteId;
        ContaId = contaId;
        TipoOperacao = tipoOperacao;
        EndToEndId = endToEndId;
        Valor = valor;
        SequenciaConta = sequenciaConta;
        Estado = EstadoPedidoPix.Aceito;
        Versao = 1;
        Generation = 1;
        CriadoEm = criadoEm ?? DateTimeOffset.UtcNow;
        AtualizadoEm = CriadoEm;
    }

    public void AtribuirSequenciaConta(long sequencia)
    {
        SequenciaConta = sequencia;
    }

    public void IniciarProcessamento(string owner, DateTimeOffset agora, DateTimeOffset? leaseExpiresAt = null, long? generation = null)
    {
        if (Estado != EstadoPedidoPix.Aceito && Estado != EstadoPedidoPix.EmTriagem && Estado != EstadoPedidoPix.ResultadoIncerto)
            throw new InvalidOperationException($"Não é permitido iniciar processamento a partir do estado {Estado}.");

        OwnerVigente = owner;
        LeaseExpiresAt = leaseExpiresAt ?? agora.AddSeconds(30);
        if (generation.HasValue)
        {
            Generation = generation.Value;
        }
        Estado = EstadoPedidoPix.EmProcessamento;
        Versao++;
        AtualizadoEm = agora;
    }

    public void AssumirLeaseExpirado(string novoOwner, DateTimeOffset novoExpiresAt, DateTimeOffset agora, long? novaGeneration = null)
    {
        if (LeaseExpiresAt.HasValue && agora < LeaseExpiresAt.Value && OwnerVigente != novoOwner)
            throw new InvalidOperationException($"Não é possível assumir lease ativo pertencente a outro executor ({OwnerVigente}).");

        OwnerVigente = novoOwner;
        LeaseExpiresAt = novoExpiresAt;
        Generation = novaGeneration ?? (Generation + 1);
        Versao++;
        AtualizadoEm = agora;
    }

    public void RegistrarIntencaoEnvio(DateTimeOffset agora)
    {
        if (Estado != EstadoPedidoPix.EmProcessamento)
            throw new InvalidOperationException("Intenção de envio só pode ser registrada no estado EmProcessamento.");

        IntencaoEnvioRegistrada = true;
        AtualizadoEm = agora;
    }

    public void Confirmar(ExternalEvidence evidencia, int versaoEsperada, string ownerEsperado, DateTimeOffset agora, long? generationEsperada = null)
    {
        if (Estado == EstadoPedidoPix.Confirmado)
            return; // Idempotente

        if (Estado == EstadoPedidoPix.RejeitadoDefinitivo)
            throw new InvalidOperationException("Conflito: Pedido já está RejeitadoDefinitivo e não pode ser sobrescrito como Confirmado.");

        var permitido = TipoOperacao == TipoOperacaoPix.Cobranca
            ? (Estado == EstadoPedidoPix.Aceito || Estado == EstadoPedidoPix.EmProcessamento || Estado == EstadoPedidoPix.ResultadoIncerto || Estado == EstadoPedidoPix.EmTriagem)
            : (Estado == EstadoPedidoPix.EmProcessamento || Estado == EstadoPedidoPix.ResultadoIncerto || Estado == EstadoPedidoPix.EmTriagem);

        if (!permitido)
            throw new InvalidOperationException($"Transição inválida para Confirmado a partir do estado {Estado}.");

        ValidarOwnerEVersao(versaoEsperada, ownerEsperado, agora, generationEsperada);

        UltimaEvidencia = evidencia;
        Estado = EstadoPedidoPix.Confirmado;
        Versao++;
        AtualizadoEm = agora;
    }

    public void RejeitarDefinitivo(ExternalEvidence evidencia, int versaoEsperada, string ownerEsperado, DateTimeOffset agora, long? generationEsperada = null)
    {
        if (Estado == EstadoPedidoPix.RejeitadoDefinitivo)
            return; // Idempotente

        if (Estado == EstadoPedidoPix.Confirmado)
            throw new InvalidOperationException("Conflito: Pedido já está Confirmado e não pode ser sobrescrito como RejeitadoDefinitivo.");

        var permitido = TipoOperacao == TipoOperacaoPix.Cobranca
            ? (Estado == EstadoPedidoPix.Aceito || Estado == EstadoPedidoPix.EmProcessamento || Estado == EstadoPedidoPix.ResultadoIncerto || Estado == EstadoPedidoPix.EmTriagem)
            : (Estado == EstadoPedidoPix.EmProcessamento || Estado == EstadoPedidoPix.ResultadoIncerto || Estado == EstadoPedidoPix.EmTriagem);

        if (!permitido)
            throw new InvalidOperationException($"Transição inválida para RejeitadoDefinitivo a partir do estado {Estado}.");

        ValidarOwnerEVersao(versaoEsperada, ownerEsperado, agora, generationEsperada);

        UltimaEvidencia = evidencia;
        Estado = EstadoPedidoPix.RejeitadoDefinitivo;
        Versao++;
        AtualizadoEm = agora;
    }

    public void ReverterParaAceitoAposFalhaPreEnvio(string motivo, DateTimeOffset agora)
    {
        if (Estado != EstadoPedidoPix.EmProcessamento)
            throw new InvalidOperationException("Apenas pedidos em processamento podem retornar a Aceito.");

        if (IntencaoEnvioRegistrada)
            throw new InvalidOperationException("Falha pós-intenção não pode reverter diretamente para Aceito; deve ir para ResultadoIncerto.");

        Estado = EstadoPedidoPix.Aceito;
        OwnerVigente = null;
        Versao++;
        AtualizadoEm = agora;
    }

    public void MarcarResultadoIncerto(string motivo, ExternalEvidence? evidencia, DateTimeOffset agora)
    {
        if (Estado != EstadoPedidoPix.EmProcessamento)
            throw new InvalidOperationException($"Transição para ResultadoIncerto só é permitida a partir de EmProcessamento (atual: {Estado}).");

        Estado = EstadoPedidoPix.ResultadoIncerto;
        UltimaEvidencia = evidencia;
        Versao++;
        AtualizadoEm = agora;
    }

    public void EncaminharParaTriagem(string motivo, DateTimeOffset agora)
    {
        if (Estado != EstadoPedidoPix.ResultadoIncerto)
            throw new InvalidOperationException($"Apenas pedidos com ResultadoIncerto podem ser encaminhados para Triagem (atual: {Estado}).");

        Estado = EstadoPedidoPix.EmTriagem;
        Versao++;
        AtualizadoEm = agora;
    }

    public void RetornarExcepcionalAoProcessamento(string owner, ExternalEvidence evidencia, DateTimeOffset agora)
    {
        if (Estado != EstadoPedidoPix.ResultadoIncerto && Estado != EstadoPedidoPix.EmTriagem)
            throw new InvalidOperationException($"Retorno excepcional ao processamento só é permitido a partir de ResultadoIncerto ou EmTriagem (atual: {Estado}).");

        OwnerVigente = owner;
        UltimaEvidencia = evidencia;
        Estado = EstadoPedidoPix.EmProcessamento;
        Versao++;
        AtualizadoEm = agora;
    }

    private void ValidarOwnerEVersao(int versaoEsperada, string ownerEsperado, DateTimeOffset agora, long? generationEsperada = null)
    {
        if (versaoEsperada != Versao)
            throw new InvalidOperationException($"Concorrência detectada: versão esperada {versaoEsperada}, versão atual {Versao}.");

        if (!string.IsNullOrEmpty(OwnerVigente) && OwnerVigente != ownerEsperado)
            throw new InvalidOperationException($"Concorrência detectada: owner esperado '{ownerEsperado}', owner atual '{OwnerVigente}'.");

        if (LeaseExpiresAt.HasValue && agora >= LeaseExpiresAt.Value)
            throw new InvalidOperationException($"Lease expirado em {LeaseExpiresAt.Value} (agora: {agora}). O executor perdeu a autoridade para concluir a operação.");

        if (generationEsperada.HasValue && generationEsperada.Value != Generation)
            throw new InvalidOperationException($"Concorrência detectada: generation esperada {generationEsperada.Value}, generation atual {Generation}.");

        if (string.IsNullOrEmpty(OwnerVigente))
        {
            OwnerVigente = ownerEsperado;
        }
    }

    public static bool IsAuthorizedReconciler(string? owner)
    {
        if (string.IsNullOrWhiteSpace(owner))
            return false;

        return owner.StartsWith("ReconciliationWorker", StringComparison.OrdinalIgnoreCase)
            || owner.StartsWith("Reconcil", StringComparison.OrdinalIgnoreCase)
            || owner.Contains("Reconcil", StringComparison.OrdinalIgnoreCase);
    }
}
