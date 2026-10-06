using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Outbank.Connector;
using Pix.Processing.Application.Features.ExecutePayment;
using Pix.Processing.Application.Ports;
using Pix.Processing.Infrastructure;
using Pix.Processing.Infrastructure.Adapters;

var builder = Host.CreateApplicationBuilder(args);

var sqlConn = builder.Configuration.GetConnectionString("Sql") ?? builder.Configuration.GetConnectionString("SqlServer");
if (!string.IsNullOrEmpty(sqlConn))
{
    builder.Services.AddDbContext<PixDbContext>(opt => opt.UseSqlServer(sqlConn));
}
else
{
    builder.Services.AddDbContext<PixDbContext>(opt => opt.UseInMemoryDatabase("PixProcessingDb"));
}

var sbConn = builder.Configuration.GetConnectionString("ServiceBus");
if (!string.IsNullOrEmpty(sbConn))
{
    builder.Services.AddSingleton(_ => new ServiceBusClient(sbConn));
}

builder.Services.AddScoped<IExecutionStore, ExecutionStore>();
builder.Services.AddSingleton<OutbankCircuitBreaker>();
builder.Services.AddSingleton<IOutbankAcl, OutbankAclAdapter>();
builder.Services.AddScoped<ExecutePaymentUseCase>();

var queueName = builder.Configuration["ServiceBus:Queues:CashOutRequested"]
    ?? builder.Configuration["ServiceBus:QueueName"]
    ?? "pix-cashout-requested";

builder.Services.AddHostedService(sp => new PaymentExecutionWorker(
    sp,
    sp.GetRequiredService<ILogger<PaymentExecutionWorker>>(),
    sp.GetService<ServiceBusClient>(),
    queueName));

var host = builder.Build();
host.Run();

public partial class Program { }
