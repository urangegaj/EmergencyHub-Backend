using Confluent.Kafka;
using FireService.Data;
using FireService.Features.GetCases;
using FireService.Features.GetCase;
using FireService.Features.UpdateCase;
using FireService.Features.GetUnits;
using FireService.Features.UpdateUnitStatus;
using FireService.Features.EmergencyAssigned;
using FireService.Kafka;
using FireService.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Shared.Redis;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(o =>
{
    o.ListenAnyIP(5003, l => l.Protocols = HttpProtocols.Http2);
});

builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));
builder.Services.AddSingleton<IDistributedLock, RedisDistributedLock>();
builder.Services.AddSingleton<IRedisCache, RedisCache>();
builder.Services.AddGrpc();

builder.Services.AddDbContext<FireDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("FireDb")));

builder.Services.Configure<KafkaSettings>(
    builder.Configuration.GetSection(KafkaSettings.SectionName));

builder.Services.AddSingleton<IProducer<string, string>>(_ =>
{
    var config = new ProducerConfig
    {
        BootstrapServers = builder.Configuration["Kafka:BootstrapServers"]
    };
    return new ProducerBuilder<string, string>(config).Build();
});

builder.Services.AddHostedService<EmergencyAssignedConsumer>();

builder.Services.AddScoped<IGetCasesHandler, GetCasesHandler>();
builder.Services.AddScoped<IGetCaseHandler, GetCaseHandler>();
builder.Services.AddScoped<IUpdateCaseHandler, UpdateCaseHandler>();
builder.Services.AddScoped<IGetUnitsHandler, GetUnitsHandler>();
builder.Services.AddScoped<IUpdateUnitStatusHandler, UpdateUnitStatusHandler>();
builder.Services.AddScoped<IEmergencyAssignedHandler, EmergencyAssignedHandler>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FireDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db);
}

app.MapGrpcService<FireGrpcService>();
app.MapGet("/", () => "FireService gRPC");

app.Run();
