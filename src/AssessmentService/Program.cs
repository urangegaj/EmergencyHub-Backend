using AssessmentService.Data;
using AssessmentService.Kafka;
using EmergencyService.Grpc;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(o =>
{
    o.ListenAnyIP(5007, l => l.Protocols = HttpProtocols.Http2);
});

builder.Services.AddGrpc();

builder.Services.AddDbContext<AssessmentDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("AssessmentDb")));

builder.Services.Configure<KafkaSettings>(
    builder.Configuration.GetSection(KafkaSettings.SectionName));

builder.Services.AddSingleton<AssignmentCache>();

builder.Services.AddGrpcClient<Emergency.EmergencyClient>(o =>
    o.Address = new Uri(builder.Configuration["Services:EmergencyService"]!));

builder.Services.AddHostedService<EmergencyAssignedConsumer>();
builder.Services.AddHostedService<CdcEmergenciesConsumer>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AssessmentDbContext>();
    await db.Database.MigrateAsync();
}

app.MapGet("/", () => "AssessmentService gRPC");

app.Run();
