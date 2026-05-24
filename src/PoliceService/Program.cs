using Confluent.Kafka;
using PoliceService.Data;
using PoliceService.Features.GetCases;
using PoliceService.Features.GetCase;
using PoliceService.Features.UpdateCase;
using PoliceService.Features.GetUnits;
using PoliceService.Features.UpdateUnitStatus;
using PoliceService.Features.EmergencyAssigned;
using PoliceService.Kafka;
using PoliceService.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Shared.Redis;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(o =>
{
    o.ListenAnyIP(5004, l => l.Protocols = HttpProtocols.Http2);
});

builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));
builder.Services.AddSingleton<IDistributedLock, RedisDistributedLock>();
builder.Services.AddSingleton<IRedisCache, RedisCache>();
builder.Services.AddGrpc();

builder.Services.AddDbContext<PoliceDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PoliceDb")));

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
    var db = scope.ServiceProvider.GetRequiredService<PoliceDbContext>();
    await db.Database.MigrateAsync();
    if (app.Environment.IsDevelopment())
        await DbSeeder.SeedAsync(db);
}

app.MapGrpcService<PoliceGrpcService>();
app.MapGet("/", () => "PoliceService gRPC");

app.Run();
