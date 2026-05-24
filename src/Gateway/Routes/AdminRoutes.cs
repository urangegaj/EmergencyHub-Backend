using AuthService;
using Gateway.Middleware;
using Grpc.Core;

namespace Gateway.Routes;

public static class AdminRoutes
{
    public static void MapAdminRoutes(this WebApplication app)
    {
        app.MapGet("/api/admin/users", async (
            string? role,
            string? department,
            int? page,
            int? pageSize,
            Auth.AuthClient auth,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var tenant = Tenant(ctx);
            if (tenant.Role != "Admin") return Results.Forbid();
            try
            {
                var req = new ListUsersRequest
                {
                    Role = role ?? "",
                    Department = department ?? "",
                    Page = page ?? 1,
                    PageSize = pageSize ?? 20
                };
                var resp = await auth.ListUsersAsync(req, AuthMeta(tenant, ct));
                return Results.Ok(resp);
            }
            catch (RpcException ex) { return MapRpcError(ex); }
        }).RequireAuthorization();

        app.MapPost("/api/admin/users", async (
            CreateUserBody body,
            Auth.AuthClient auth,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var tenant = Tenant(ctx);
            if (tenant.Role != "Admin") return Results.Forbid();
            try
            {
                var req = new CreateUserRequest
                {
                    Email = body.Email,
                    Password = body.Password,
                    Role = body.Role,
                    FirstName = body.FirstName,
                    LastName = body.LastName
                };
                if (body.Department is not null) req.Department = body.Department;
                if (body.Phone is not null) req.Phone = body.Phone;

                var resp = await auth.CreateUserAsync(req, AuthMeta(tenant, ct));
                return Results.Created($"/api/admin/users/{resp.UserId}", new { userId = resp.UserId });
            }
            catch (RpcException ex) { return MapRpcError(ex); }
        }).RequireAuthorization();

        app.MapPatch("/api/admin/users/{id}", async (
            string id,
            UpdateUserBody body,
            Auth.AuthClient auth,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var tenant = Tenant(ctx);
            if (tenant.Role != "Admin") return Results.Forbid();
            if (!Guid.TryParse(id, out _)) return Results.BadRequest("id must be a valid GUID.");
            try
            {
                var req = new UpdateUserRequest { UserId = id };
                if (body.FirstName is not null) req.FirstName = body.FirstName;
                if (body.LastName is not null) req.LastName = body.LastName;
                if (body.Phone is not null) req.Phone = body.Phone;
                if (body.Department is not null) req.Department = body.Department;

                await auth.UpdateUserAsync(req, AuthMeta(tenant, ct));
                return Results.NoContent();
            }
            catch (RpcException ex) { return MapRpcError(ex); }
        }).RequireAuthorization();

        app.MapDelete("/api/admin/users/{id}", async (
            string id,
            Auth.AuthClient auth,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var tenant = Tenant(ctx);
            if (tenant.Role != "Admin") return Results.Forbid();
            if (!Guid.TryParse(id, out _)) return Results.BadRequest("id must be a valid GUID.");
            try
            {
                await auth.DeactivateUserAsync(new DeactivateUserRequest { UserId = id }, AuthMeta(tenant, ct));
                return Results.NoContent();
            }
            catch (RpcException ex) { return MapRpcError(ex); }
        }).RequireAuthorization();

        app.MapPatch("/api/admin/users/{id}/role", async (
            string id,
            AssignRoleBody body,
            Auth.AuthClient auth,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var tenant = Tenant(ctx);
            if (tenant.Role != "Admin") return Results.Forbid();
            if (!Guid.TryParse(id, out _)) return Results.BadRequest("id must be a valid GUID.");
            try
            {
                var req = new AssignRoleRequest { UserId = id, Role = body.Role };
                if (body.Department is not null) req.Department = body.Department;

                await auth.AssignRoleAsync(req, AuthMeta(tenant, ct));
                return Results.NoContent();
            }
            catch (RpcException ex) { return MapRpcError(ex); }
        }).RequireAuthorization();
    }

    private static TenantContext Tenant(HttpContext ctx) =>
        (TenantContext?)ctx.Items[TenantContextMiddleware.ItemsKey]
            ?? throw new InvalidOperationException("TenantContext missing.");

    private static CallOptions AuthMeta(TenantContext tenant, CancellationToken ct)
    {
        var meta = new Metadata { { "city_id", tenant.CityId.ToString() } };
        return new CallOptions(headers: meta, cancellationToken: ct);
    }

    private static IResult MapRpcError(RpcException ex) => ex.StatusCode switch
    {
        StatusCode.NotFound        => Results.NotFound(ex.Status.Detail),
        StatusCode.InvalidArgument => Results.BadRequest(ex.Status.Detail),
        StatusCode.Unauthenticated => Results.Unauthorized(),
        StatusCode.AlreadyExists   => Results.Conflict(ex.Status.Detail),
        StatusCode.PermissionDenied => Results.Forbid(),
        StatusCode.Unavailable     => Results.StatusCode(503),
        _                          => Results.Problem(ex.Status.Detail, statusCode: 500)
    };
}

record CreateUserBody(string Email, string Password, string Role, string FirstName, string LastName, string? Department, string? Phone);
record UpdateUserBody(string? FirstName, string? LastName, string? Phone, string? Department);
record AssignRoleBody(string Role, string? Department);
