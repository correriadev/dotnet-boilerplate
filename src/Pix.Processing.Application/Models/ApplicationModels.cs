using Pix.Processing.Domain.Enums;
using Pix.Processing.Domain.ValueObjects;

namespace Pix.Processing.Application.Models;

public enum StatusAquisicao
{
    Sucesso = 1,
    Conflito = 2,
    JaConcluido = 3
}

public record AquisicaoResultado(
    StatusAquisicao Status,
    EstadoPedidoPix? EstadoAtual = null,
    string? Mensagem = null);

public enum StatusAdmissao
{
    Admitido = 1,
    LimiteAtingido = 2,
    Indisponivel = 3
}

public record AdmissaoResultado(
    StatusAdmissao Status,
    string? Mensagem = null);

public record CacheGeneration(long Numero);

public record FinancialSnapshot(
    ContaId ContaId,
    Money Saldo,
    Money LimiteDisponivel,
    DateTimeOffset ObservadoEm,
    CacheGeneration Geracao,
    DateTimeOffset ValidoAte);

public record PendingCommandItem(
    string EventId,
    Guid PedidoId,
    ContaId ContaId,
    long Sequencia,
    string CorpoJson,
    string Status,
    int Tentativas,
    DateTimeOffset ProximaTentativa);
