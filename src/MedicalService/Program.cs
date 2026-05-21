using Confluent.Kafka;
using MedicalService.Data;
using MedicalService.Kafka;
using MedicalService.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(o =>
{
    o.ListenAnyIP(5005, l => l.Protocols = HttpProtocols.Http2);
});

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
