using Microsoft.EntityFrameworkCore;
using Pix.Callbacks.Application;
using Pix.Callbacks.Infrastructure;
using Webhook.Dispatcher;

var builder = Host.CreateApplicationBuilder(args);

var sqlConn = builder.Configuration.GetConnectionString("Sql") ?? builder.Configuration.GetConnectionString("SqlServer");
if (!string.IsNullOrEmpty(sqlConn))
{
    builder.Services.AddDbContext<CallbacksDbContext>(opt => opt.UseSqlServer(sqlConn));
}
else
{
    builder.Services.AddDbContext<CallbacksDbContext>(opt => opt.UseInMemoryDatabase("CallbacksDb"));
}

builder.Services.AddScoped<ICallbackDeliveryStore, CallbackDeliveryStore>();
builder.Services.AddHttpClient<ISafeCallbackTransport, SafeCallbackTransport>();
builder.Services.AddScoped<DeliverCallbackUseCase>();

builder.Services.AddHostedService<WebhookDeliveryWorker>();

var host = builder.Build();
host.Run();
