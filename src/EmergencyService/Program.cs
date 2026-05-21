using EmergencyService.Data;
using EmergencyService.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();

builder.Services.AddDbContext<EmergencyDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("EmergencyDb")));

builder.WebHost.ConfigureKestrel(o =>
    o.ListenAnyIP(5005, l => l.Protocols = HttpProtocols.Http2));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<EmergencyDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db);
}

app.MapGrpcService<EmergencyGrpcService>();

app.Run();
