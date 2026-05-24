using AuthService;
using EmergencyService.Grpc;
using Microsoft.EntityFrameworkCore;
using NotificationService.Data;
using NotificationService.Kafka;
using NotificationService.Kafka.Consumers;
using NotificationService.Services;
using NotificationService.Services.Email;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("NotificationDb")));

builder.Services.Configure<KafkaSettings>(
    builder.Configuration.GetSection(KafkaSettings.SectionName));

builder.Services.Configure<SmtpSettings>(
    builder.Configuration.GetSection(SmtpSettings.SectionName));

builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<AuthUserLookupService>();
builder.Services.AddScoped<NotificationDispatchService>();

builder.Services.AddGrpcClient<Auth.AuthClient>(o =>
    o.Address = new Uri(builder.Configuration["Services:AuthService"]!));

builder.Services.AddGrpcClient<Emergency.EmergencyClient>(o =>
    o.Address = new Uri(builder.Configuration["Services:EmergencyService"]!));

builder.Services.AddHostedService<EmergencyCreatedConsumer>();
builder.Services.AddHostedService<EmergencyAssignedConsumer>();
builder.Services.AddHostedService<EmergencyStatusUpdatedConsumer>();
builder.Services.AddHostedService<DepartmentCaseUpdatedConsumer>();
builder.Services.AddHostedService<RetryFailedJobsWorker>();

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
    await db.Database.MigrateAsync();
    if (host.Services.GetRequiredService<IHostEnvironment>().IsDevelopment())
        await DbSeeder.SeedAsync(db);
}

host.Run();
