using Confluent.Kafka;
using PoliceService.Data;
using PoliceService.Kafka;
using PoliceService.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(o =>
{
    o.ListenAnyIP(5004, l => l.Protocols = HttpProtocols.Http2);
});

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
