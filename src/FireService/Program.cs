using FireService.Data;
using FireService.Kafka;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(o =>
{
    o.ListenAnyIP(5003, l => l.Protocols = HttpProtocols.Http2);
});

builder.Services.AddGrpc();

builder.Services.AddDbContext<FireDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("FireDb")));

builder.Services.Configure<KafkaSettings>(
    builder.Configuration.GetSection(KafkaSettings.SectionName));

var app = builder.Build();

app.MapGet("/", () => "FireService gRPC");

app.Run();
