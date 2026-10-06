using Pix.Processing.Domain.Enums;
using Pix.Processing.Domain.ValueObjects;

namespace Pix.Processing.Domain.Entities;

public class CoordenacaoConta
{
    private readonly HashSet<string> _motivosBloqueio = new();

    public ContaId ContaId { get; private set; }
    public long ProximaSequenciaAtribuivel { get; private set; }
    public long ProximaSequenciaExecutavel { get; private set; }
    public EstadoCoordenacao Estado { get; private set; }
    public int Versao { get; private set; }
    public DateTimeOffset AtualizadoEm { get; private set; }

    public IReadOnlyCollection<string> MotivosBloqueio => _motivosBloqueio;

    // EF constructor
    private CoordenacaoConta() { }

    public CoordenacaoConta(ContaId contaId, long sequenciaInicial = 1, DateTimeOffset? criadoEm = null)
    {
        if (sequenciaInicial <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequenciaInicial), "Sequência inicial deve ser positiva (> 0).");

        ContaId = contaId;
        ProximaSequenciaAtribuivel = sequenciaInicial;
        ProximaSequenciaExecutavel = sequenciaInicial;
        Estado = EstadoCoordenacao.Livre;
        Versao = 1;
        AtualizadoEm = criadoEm ?? DateTimeOffset.UtcNow;
    }

    public AccountSequence AtribuirProximaSequencia(DateTimeOffset agora)
    {
        var seq = new AccountSequence(ContaId, ProximaSequenciaAtribuivel);
        ProximaSequenciaAtribuivel++;
        Versao++;
        AtualizadoEm = agora;
        return seq;
    }

    public void Bloquear(long sequencia, string motivo, DateTimeOffset agora)
    {
        var chaveBloqueio = $"{sequencia}:{motivo}";
        _motivosBloqueio.Add(chaveBloqueio);
        Estado = EstadoCoordenacao.Bloqueada;
        Versao++;
        AtualizadoEm = agora;
    }

    public bool Desbloquear(long sequencia, string motivo, DateTimeOffset agora)
    {
        var chaveBloqueio = $"{sequencia}:{motivo}";
        if (!_motivosBloqueio.Remove(chaveBloqueio))
        {
            var prefixo = $"{sequencia}:";
            var chaves = _motivosBloqueio.Where(k => k.StartsWith(prefixo)).ToList();
            foreach (var k in chaves)
            {
                _motivosBloqueio.Remove(k);
            }
        }

        // Se ainda restam outras razões de bloqueio (ex: outra sequência incerta), permanece bloqueada (INV-19)
        if (_motivosBloqueio.Count == 0)
        {
            Estado = EstadoCoordenacao.Livre;
        }

        Versao++;
        AtualizadoEm = agora;

        return Estado == EstadoCoordenacao.Livre;
    }

    public void AvancarExecucao(long sequenciaExecutada, DateTimeOffset agora)
    {
        if (sequenciaExecutada != ProximaSequenciaExecutavel)
            throw new InvalidOperationException($"Sequência executada {sequenciaExecutada} não coincide com a próxima executável {ProximaSequenciaExecutavel}.");

        ProximaSequenciaExecutavel++;
        Versao++;
        AtualizadoEm = agora;
    }
}
