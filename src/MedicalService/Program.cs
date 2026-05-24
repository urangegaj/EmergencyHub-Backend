using Confluent.Kafka;
using MedicalService.Data;
using MedicalService.Features.GetCases;
using MedicalService.Features.GetCase;
using MedicalService.Features.UpdateCase;
using MedicalService.Features.GetUnits;
using MedicalService.Features.UpdateUnitStatus;
using MedicalService.Features.EmergencyAssigned;
using MedicalService.Kafka;
using MedicalService.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Shared.Redis;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(o =>
{
    o.ListenAnyIP(5006, l => l.Protocols = HttpProtocols.Http2);
});

builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));
builder.Services.AddSingleton<IDistributedLock, RedisDistributedLock>();
builder.Services.AddSingleton<IRedisCache, RedisCache>();
builder.Services.AddGrpc();

builder.Services.AddDbContext<MedicalDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("MedicalDb")));

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
    var db = scope.ServiceProvider.GetRequiredService<MedicalDbContext>();
    await db.Database.MigrateAsync();
    if (app.Environment.IsDevelopment())
        await DbSeeder.SeedAsync(db);
}

app.MapGrpcService<MedicalGrpcService>();
app.MapGet("/", () => "MedicalService gRPC");

app.Run();
