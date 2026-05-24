using EmergencyService.Grpc;
using Gateway.Extensions;
using Gateway.Middleware;
using Grpc.Core;

namespace Gateway.Routes;

public static class EmergencyRoutes
{
    public static void MapEmergencyRoutes(this WebApplication app)
    {
        app.MapPost("/api/emergencies", async (
            CreateEmergencyBody body,
            Emergency.EmergencyClient emergency,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var tenant = Tenant(ctx);
            if (string.IsNullOrWhiteSpace(body.EmergencyTypeId))
                return Results.BadRequest("emergencyTypeId is required.");
            if (string.IsNullOrWhiteSpace(body.Description))
                return Results.BadRequest("description is required.");
            if (string.IsNullOrWhiteSpace(body.Address))
                return Results.BadRequest("address is required.");

            try
            {
                var req = new CreateEmergencyRequest
                {
                    CityId = tenant.CityId.ToString(),
                    ReportedByUserId = tenant.UserId.ToString(),
                    EmergencyTypeId = body.EmergencyTypeId,
                    Description = body.Description,
                    Address = body.Address
                };

                var resp = await emergency.CreateEmergencyAsync(req, ct.ToCallOptions());
                return Results.Created($"/api/emergencies/{resp.Id}", resp);
            }
            catch (RpcException ex) { return MapRpcError(ex); }
        }).RequireAuthorization();

        app.MapGet("/api/emergencies", async (
            string? status,
            string? typeName,
            long? fromTs,
            long? toTs,
            string? q,
            int? page,
            int? pageSize,
            string? sortBy,
            string? order,
            Emergency.EmergencyClient emergency,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var tenant = Tenant(ctx);
            try
            {
                var req = new ListEmergenciesRequest
                {
                    CityId = tenant.CityId.ToString(),
                    Status = status ?? "",
                    TypeName = typeName ?? "",
                    FromTs = fromTs ?? 0,
                    ToTs = toTs ?? 0,
                    Q = q ?? "",
                    Page = page ?? 1,
                    PageSize = pageSize ?? 20,
                    SortBy = sortBy ?? "created_at",
                    Order = order ?? "desc"
                };
                var resp = await emergency.ListEmergenciesAsync(req, ct.ToCallOptions());
                return Results.Ok(resp);
            }
            catch (RpcException ex) { return MapRpcError(ex); }
        }).RequireAuthorization();

        app.MapGet("/api/emergencies/{id}", async (
            string id,
            Emergency.EmergencyClient emergency,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            if (ValidateEmergencyId(id) is { } idError)
                return idError;

            var tenant = Tenant(ctx);
            try
            {
                var resp = await emergency.GetEmergencyAsync(
                    new GetEmergencyRequest
                    {
                        EmergencyId = id,
                        CityId = tenant.CityId.ToString()
                    },
                    ct.ToCallOptions());
                return Results.Ok(resp);
            }
            catch (RpcException ex) { return MapRpcError(ex); }
        }).RequireAuthorization();

        app.MapGet("/api/emergencies/{id}/poll", async (
            string id,
            int since,
            int? timeout,
            Emergency.EmergencyClient emergency,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            if (ValidateEmergencyId(id) is { } idError)
                return idError;

            var tenant = Tenant(ctx);
            try
            {
                var resp = await emergency.PollEmergencyAsync(
                    new PollEmergencyRequest
                    {
                        EmergencyId    = id,
                        CityId         = tenant.CityId.ToString(),
                        Since          = since,
                        TimeoutSeconds = timeout ?? 30
                    },
                    ct.ToCallOptions());
                return Results.Ok(resp);
            }
            catch (RpcException ex) { return MapRpcError(ex); }
        }).RequireAuthorization();

        app.MapPost("/api/emergencies/{id}/assign", async (
            string id,
            AssignEmergencyBody body,
            Emergency.EmergencyClient emergency,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            if (ValidateEmergencyId(id) is { } idError)
                return idError;

            if (body.Departments is null || body.Departments.Length == 0)
                return Results.BadRequest("At least one department is required.");

            var tenant = Tenant(ctx);
            try
            {
                var req = new AssignEmergencyRequest
                {
                    EmergencyId = id,
                    CityId = tenant.CityId.ToString(),
                    AssignedByUserId = tenant.UserId.ToString()
                };
                req.Departments.AddRange(body.Departments);

                var resp = await emergency.AssignEmergencyAsync(req, ct.ToCallOptions());
                return Results.Ok(resp);
            }
            catch (RpcException ex) { return MapRpcError(ex); }
        }).RequireAuthorization();
    }

    private static TenantContext Tenant(HttpContext ctx) =>
        (TenantContext?)ctx.Items[TenantContextMiddleware.ItemsKey]
            ?? throw new InvalidOperationException("TenantContext missing.");

    private static IResult? ValidateEmergencyId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return Results.BadRequest("Emergency id is required.");

        if (!Guid.TryParse(id, out _))
            return Results.BadRequest("Emergency id must be a valid GUID.");

        return null;
    }

    private static IResult MapRpcError(RpcException ex) => ex.StatusCode switch
    {
        StatusCode.NotFound        => Results.NotFound(ex.Status.Detail),
        StatusCode.InvalidArgument => Results.BadRequest(ex.Status.Detail),
        StatusCode.Unauthenticated => Results.Unauthorized(),
        StatusCode.AlreadyExists   => Results.Conflict(ex.Status.Detail),
        _                          => Results.Problem(ex.Status.Detail, statusCode: 500)
    };
}

record CreateEmergencyBody(string EmergencyTypeId, string Description, string Address);
record AssignEmergencyBody(string[] Departments);
