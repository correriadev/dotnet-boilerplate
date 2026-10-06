using Microsoft.EntityFrameworkCore;
using Pix.Processing.Application.Features.ExecutePayment;
using Pix.Processing.Application.Features.ReconcilePayment;
using Pix.Processing.Application.Ports;
using Pix.Processing.Infrastructure;
using Pix.Processing.Infrastructure.Adapters;
using Pix.Reconciliation.Worker;

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

builder.Services.AddScoped<IExecutionStore, ExecutionStore>();
builder.Services.AddSingleton<IOutbankAcl, OutbankAclAdapter>();
builder.Services.AddScoped<ReconcilePaymentUseCase>();
builder.Services.AddScoped<ExecutePaymentUseCase>();

builder.Services.AddHostedService<ReconciliationWorker>();

var host = builder.Build();
host.Run();
