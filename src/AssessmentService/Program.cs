using AssessmentService.Data;
using AssessmentService.Features.GetReport;
using AssessmentService.Features.RetryReport;
using AssessmentService.Features.EmergencyCompletion;
using AssessmentService.Kafka;
using AssessmentService.Services;
using EmergencyService.Grpc;
using FireService;
using MedicalService;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using PoliceService;

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

builder.Services.AddGrpcClient<Police.PoliceClient>(o =>
    o.Address = new Uri(builder.Configuration["Services:PoliceService"]!));

builder.Services.AddGrpcClient<Fire.FireClient>(o =>
    o.Address = new Uri(builder.Configuration["Services:FireService"]!));

builder.Services.AddGrpcClient<Medical.MedicalClient>(o =>
    o.Address = new Uri(builder.Configuration["Services:MedicalService"]!));

builder.Services.AddSingleton<AssessmentPipelineService>();

builder.Services.AddHostedService<EmergencyAssignedConsumer>();
builder.Services.AddHostedService<CdcEmergenciesConsumer>();

builder.Services.AddScoped<IGetReportHandler, GetReportHandler>();
builder.Services.AddScoped<IRetryReportHandler, RetryReportHandler>();
builder.Services.AddScoped<IEmergencyCompletionHandler, EmergencyCompletionHandler>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AssessmentDbContext>();
    await db.Database.MigrateAsync();
}

app.MapGrpcService<AssessmentGrpcService>();
app.MapGet("/", () => "AssessmentService gRPC");

app.Run();
