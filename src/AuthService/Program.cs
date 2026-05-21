using AuthService.Data;
using AuthService.Services;
using Microsoft.EntityFrameworkCore;
using Shared.Auth;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();

builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("AuthDb")));

builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));

builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<JwtBlacklistService>();

builder.WebHost.ConfigureKestrel(o =>
{
    o.ListenAnyIP(5001, l => l.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2);
    o.ListenAnyIP(5002, l => l.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1);
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db);
}

app.MapGrpcService<AuthGrpcService>();

app.MapGet("/.well-known/jwks.json", (TokenService tokens) =>
    Results.Ok(tokens.GetJwks()));

app.MapGet("/", () => "AuthService gRPC");

app.Run();
