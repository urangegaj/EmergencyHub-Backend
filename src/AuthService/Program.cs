using AuthService.Data;
using AuthService.Features.Register;
using AuthService.Features.Login;
using AuthService.Features.RefreshToken;
using AuthService.Features.Logout;
using AuthService.Features.GetUser;
using AuthService.Features.ListDepartmentUsers;
using AuthService.Features.ListUsers;
using AuthService.Features.CreateUser;
using AuthService.Features.UpdateUser;
using AuthService.Features.DeactivateUser;
using AuthService.Features.AssignRole;
using AuthService.Services;
using Microsoft.EntityFrameworkCore;
using JwtBlacklist;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();

builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("AuthDb")));

builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));

builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<JwtBlacklistService>();

builder.Services.AddScoped<IRegisterHandler, RegisterHandler>();
builder.Services.AddScoped<ILoginHandler, LoginHandler>();
builder.Services.AddScoped<IRefreshTokenHandler, RefreshTokenHandler>();
builder.Services.AddScoped<ILogoutHandler, LogoutHandler>();
builder.Services.AddScoped<IGetUserHandler, GetUserHandler>();
builder.Services.AddScoped<IListDepartmentUsersHandler, ListDepartmentUsersHandler>();
builder.Services.AddScoped<IListUsersHandler, ListUsersHandler>();
builder.Services.AddScoped<ICreateUserHandler, CreateUserHandler>();
builder.Services.AddScoped<IUpdateUserHandler, UpdateUserHandler>();
builder.Services.AddScoped<IDeactivateUserHandler, DeactivateUserHandler>();
builder.Services.AddScoped<IAssignRoleHandler, AssignRoleHandler>();

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
