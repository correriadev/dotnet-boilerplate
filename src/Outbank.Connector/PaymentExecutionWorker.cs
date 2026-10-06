using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Pix.Processing.Application.Features.ExecutePayment;
using Pix.Processing.Domain.Enums;
using Pix.Processing.Domain.ValueObjects;
using Pix.Processing.Infrastructure;
using Pix.Processing.Infrastructure.Entities;

namespace Outbank.Connector;

public class PaymentExecutionWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PaymentExecutionWorker> _logger;
    private readonly ServiceBusClient? _serviceBusClient;
    private readonly string _queueName;
    private ServiceBusSessionProcessor? _sessionProcessor;

    public PaymentExecutionWorker(
        IServiceProvider serviceProvider,
        ILogger<PaymentExecutionWorker> logger,
        ServiceBusClient? serviceBusClient = null,
        string queueName = "pix-payments-session")
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _serviceBusClient = serviceBusClient;
        _queueName = queueName;
    }

    public async Task ProcessSessionMessageInternalAsync(ProcessSessionMessageEventArgs args, CancellationToken ct)
    {
        var accountId = args.SessionId;
        _logger.LogInformation("Processando mensagem {MessageId} na sessão da conta {AccountId}...", args.Message.MessageId, accountId);

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var useCase = scope.ServiceProvider.GetRequiredService<ExecutePaymentUseCase>();
            var db = scope.ServiceProvider.GetRequiredService<PixDbContext>();

            var body = args.Message.Body.ToString();
            var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var pedidoId = root.GetProperty("pedidoId").GetGuid();
            var endToEndIdStr = root.GetProperty("endToEndId").GetString()!;
            var endToEndId = new EndToEndId(endToEndIdStr);
            var contaId = new ContaId(accountId);
            var sequencia = root.TryGetProperty("sequencia", out var seqProp) ? seqProp.GetInt64() : 1;
            var quantia = root.GetProperty("valor").GetDecimal();
            var moeda = root.TryGetProperty("moeda", out var moedaProp) ? moedaProp.GetString()! : "BRL";
            var valor = new Money(quantia, moeda);

            // PIX-REV-03: Valida coordenação da conta antes do envio externo
            var coord = await db.CoordenacoesConta.FirstOrDefaultAsync(c => c.ContaId == contaId, ct);
            if (coord != null && (coord.Estado == EstadoCoordenacao.Bloqueada || sequencia > coord.ProximaSequenciaExecutavel))
            {
                // Lacuna de sequência ou conta bloqueada: NÃO despacha para Outbank;
                // grava o comando pendente duravelmente no banco em TX-02
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var status = coord.Estado == EstadoCoordenacao.Bloqueada ? "BLOCKED" : "PENDING";
                var pendenteExistente = await db.PendingCommands.FirstOrDefaultAsync(p => p.EventId == args.Message.MessageId, ct);
                if (pendenteExistente == null)
                {
                    db.PendingCommands.Add(new PendingCommandRecord
                    {
                        EventId = args.Message.MessageId,
                        PedidoId = pedidoId,
                        ContaId = contaId.Valor,
                        Sequencia = sequencia,
                        CorpoJson = body,
                        Status = status,
                        Tentativas = 0,
                        ProximaTentativa = DateTimeOffset.UtcNow
                    });
                    await db.SaveChangesAsync(ct);
                }
                await tx.CommitAsync(ct);

                _logger.LogWarning("Comando {MessageId} gravado como pendente duravelmente em TX-02 (status: {Status}) por lacuna ou bloqueio de conta.", args.Message.MessageId, status);
                await args.CompleteMessageAsync(args.Message, ct);
                return;
            }

            var comando = new ExecutePaymentCommand(
                pedidoId,
                contaId,
                sequencia,
                endToEndId,
                valor,
                $"OutbankConnector-SessionWorker-{accountId}",
                1,
                DateTimeOffset.UtcNow.AddMinutes(2));

            var resultado = await useCase.ExecutarAsync(comando, ct);

            // Somente completa a mensagem do broker após término bem-sucedido ou conclusão idempotente (INV-09, TX-02)
            if (resultado.Status == ExecutionResultStatus.Confirmado ||
                resultado.Status == ExecutionResultStatus.RejeitadoDefinitivo ||
                resultado.Status == ExecutionResultStatus.JaConcluido ||
                resultado.Status == ExecutionResultStatus.ResultadoIncerto)
            {
                await args.CompleteMessageAsync(args.Message, ct);
            }
            else if (resultado.Status == ExecutionResultStatus.ConflitoAquisicao ||
                     resultado.Status == ExecutionResultStatus.FalhaConsolidacao)
            {
                // PIX-REV-05: Falha de commit/consolidação ou conflito: NÃO completa a mensagem! Abandona para reprocessamento
                _logger.LogWarning("Operação retornou status {Status}. Abandonando mensagem {MessageId} para reprocessamento ordenado.", resultado.Status, args.Message.MessageId);
                await args.AbandonMessageAsync(args.Message, cancellationToken: ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro no processamento da mensagem {MessageId}. Abandonando para reprocessamento.", args.Message.MessageId);
            await args.AbandonMessageAsync(args.Message, cancellationToken: ct);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbank Connector Worker iniciado.");

        if (_serviceBusClient != null)
        {
            try
            {
                var options = new ServiceBusSessionProcessorOptions
                {
                    AutoCompleteMessages = false,
                    MaxConcurrentSessions = 5,
                    SessionIdleTimeout = TimeSpan.FromSeconds(5)
                };

                _sessionProcessor = _serviceBusClient.CreateSessionProcessor(_queueName, options);
                _sessionProcessor.ProcessMessageAsync += args => ProcessSessionMessageInternalAsync(args, stoppingToken);
                _sessionProcessor.ProcessErrorAsync += args =>
                {
                    _logger.LogError(args.Exception, "Erro no processador de sessões do Service Bus.");
                    return Task.CompletedTask;
                };

                await _sessionProcessor.StartProcessingAsync(stoppingToken);
                _logger.LogInformation("ServiceBusSessionProcessor iniciado para a fila {Queue}.", _queueName);

                // Aguarda até o cancelamento do worker
                await Task.Delay(Timeout.Infinite, stoppingToken);
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Falha ao iniciar ServiceBusSessionProcessor. Iniciando modo de compatibilidade/polling.");
            }
        }

        // Modo polling local (fallback quando broker remoto não estiver configurado)
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<PixDbContext>();
                var useCase = scope.ServiceProvider.GetRequiredService<ExecutePaymentUseCase>();

                var pedido = await db.Pedidos
                    .FirstOrDefaultAsync(p => p.Estado == Pix.Processing.Domain.Enums.EstadoPedidoPix.Aceito && p.EndToEndId != null, stoppingToken);

                if (pedido != null && pedido.EndToEndId.HasValue)
                {
                    var coord = await db.CoordenacoesConta.FirstOrDefaultAsync(c => c.ContaId == pedido.ContaId, stoppingToken);
                    if (coord != null && (coord.Estado == EstadoCoordenacao.Bloqueada || (pedido.SequenciaConta.HasValue && pedido.SequenciaConta.Value > coord.ProximaSequenciaExecutavel)))
                    {
                        _logger.LogWarning("Pedido {PedidoId} ignorado no polling por bloqueio ou lacuna de sequência na conta {ContaId}.", pedido.Id, pedido.ContaId);
                        await Task.Delay(1000, stoppingToken);
                        continue;
                    }

                    _logger.LogInformation("Executando pedido {PedidoId} via polling local...", pedido.Id);
                    var comando = new ExecutePaymentCommand(
                        pedido.Id,
                        pedido.ContaId,
                        pedido.SequenciaConta ?? 1,
                        pedido.EndToEndId.Value,
                        pedido.Valor,
                        "OutbankConnector-Worker-01",
                        pedido.Versao,
                        DateTimeOffset.UtcNow.AddMinutes(2));

                    await useCase.ExecutarAsync(comando, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no loop do PaymentExecutionWorker.");
            }

            await Task.Delay(1000, stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_sessionProcessor != null)
        {
            await _sessionProcessor.StopProcessingAsync(cancellationToken);
            await _sessionProcessor.DisposeAsync();
        }
        await base.StopAsync(cancellationToken);
    }
}
