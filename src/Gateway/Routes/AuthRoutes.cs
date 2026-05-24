using AuthService;
using Gateway.Extensions;
using Gateway.Middleware;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;

namespace Gateway.Routes;

public static class AuthRoutes
{
    public static void MapAuthRoutes(this WebApplication app)
    {
        app.MapPost("/api/auth/register", async (RegisterBody body, Auth.AuthClient auth, CancellationToken ct) =>
        {
            try
            {
                var req = new RegisterRequest
                {
                    Email = body.Email,
                    Password = body.Password,
                    Role = body.Role,
                    FirstName = body.FirstName,
                    LastName = body.LastName,
                    CityId = body.CityId
                };
                if (body.Department is not null) req.Department = body.Department;
                if (body.Phone is not null) req.Phone = body.Phone;

                var result = await auth.RegisterAsync(req, ct.ToCallOptions());
                return Results.Ok(new { userId = result.UserId });
            }
            catch (RpcException ex)
            {
                return MapRpcError(ex);
            }
        });

        app.MapPost("/api/auth/login", async (LoginBody body, Auth.AuthClient auth, CancellationToken ct) =>
        {
            try
            {
                var result = await auth.LoginAsync(
                    new LoginRequest { Email = body.Email, Password = body.Password },
                    ct.ToCallOptions());

                return Results.Ok(new
                {
                    accessToken = result.AccessToken,
                    refreshToken = result.RefreshToken,
                    userId = result.UserId,
                    cityId = result.CityId,
                    role = result.Role,
                    department = result.HasDepartment ? result.Department : null
                });
            }
            catch (RpcException ex)
            {
                return MapRpcError(ex);
            }
        });

        app.MapPost("/api/auth/refresh", async (RefreshBody body, Auth.AuthClient auth, CancellationToken ct) =>
        {
            try
            {
                var result = await auth.RefreshAsync(
                    new RefreshRequest { RefreshToken = body.RefreshToken },
                    ct.ToCallOptions());

                return Results.Ok(new
                {
                    accessToken = result.AccessToken,
                    refreshToken = result.RefreshToken,
                    userId = result.UserId,
                    cityId = result.CityId,
                    role = result.Role,
                    department = result.HasDepartment ? result.Department : null
                });
            }
            catch (RpcException ex)
            {
                return MapRpcError(ex);
            }
        });

        app.MapPost("/api/auth/logout", async (HttpContext http, LogoutBody body, Auth.AuthClient auth, CancellationToken ct) =>
        {
            try
            {
                var req = new LogoutRequest { RefreshToken = body.RefreshToken };
                var accessToken = ExtractBearerToken(http) ?? body.AccessToken;
                if (!string.IsNullOrWhiteSpace(accessToken))
                    req.AccessToken = accessToken;

                await auth.LogoutAsync(req, ct.ToCallOptions());
                return Results.Ok();
            }
            catch (RpcException ex)
            {
                return MapRpcError(ex);
            }
        });

        app.MapGet("/api/me", (HttpContext ctx) =>
        {
            var tenant = (TenantContext?)ctx.Items[TenantContextMiddleware.ItemsKey];
            return Results.Ok(tenant);
        }).RequireAuthorization();
    }

    private static string? ExtractBearerToken(HttpContext ctx)
    {
        var header = ctx.Request.Headers.Authorization.FirstOrDefault();
        if (header is null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;
        return header["Bearer ".Length..].Trim();
    }

    private static IResult MapRpcError(RpcException ex) => ex.StatusCode switch
    {
        Grpc.Core.StatusCode.AlreadyExists => Results.Conflict(ex.Status.Detail),
        Grpc.Core.StatusCode.InvalidArgument => Results.BadRequest(ex.Status.Detail),
        Grpc.Core.StatusCode.NotFound => Results.NotFound(ex.Status.Detail),
        Grpc.Core.StatusCode.Unauthenticated => Results.Unauthorized(),
        Grpc.Core.StatusCode.PermissionDenied => Results.Forbid(),
        _ => Results.Problem(ex.Status.Detail, statusCode: 500)
    };
}

record RegisterBody(string Email, string Password, string Role, string FirstName, string LastName, string CityId, string? Department, string? Phone);
record LoginBody(string Email, string Password);
record RefreshBody(string RefreshToken);
record LogoutBody(string RefreshToken, string? AccessToken = null);
