using Gateway.Middleware;
using Gateway.Routes;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using JwtBlacklist;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

var jwksUri = builder.Configuration["Jwt:JwksUri"]!;
using var http = new HttpClient();
var jwksJson = await http.GetStringAsync(jwksUri);
var jwks = new JsonWebKeySet(jwksJson);
var signingKeys = jwks.GetSigningKeys();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = signingKeys,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));
builder.Services.AddSingleton<JwtBlacklistService>();

builder.Services.AddGrpcClient<AuthService.Auth.AuthClient>(o =>
    o.Address = new Uri(builder.Configuration["Services:AuthService"]!));

builder.Services.AddGrpcClient<PoliceService.Police.PoliceClient>(o =>
    o.Address = new Uri(builder.Configuration["Services:PoliceService"]!));
builder.Services.AddGrpcClient<FireService.Fire.FireClient>(o =>
    o.Address = new Uri(builder.Configuration["Services:FireService"]!));

builder.Services.AddGrpcClient<MedicalService.Medical.MedicalClient>(o =>
    o.Address = new Uri(builder.Configuration["Services:MedicalService"]!));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Name = "Authorization"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            []
        }
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<RequestLoggingMiddleware>();
app.UseAuthentication();
app.UseMiddleware<JwtBlacklistMiddleware>();
app.UseAuthorization();
app.UseMiddleware<TenantContextMiddleware>();

app.MapAuthRoutes();
app.MapFireRoutes();
app.MapPoliceRoutes();
app.MapMedicalRoutes();

app.Run();
