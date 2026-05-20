using FireService;
using Gateway.Extensions;
using Gateway.Middleware;
using Grpc.Core;

namespace Gateway.Routes;

public static class FireRoutes
{
    public static void MapFireRoutes(this WebApplication app)
    {
        app.MapGet("/api/fire/cases", async (
            string? status,
            Fire.FireClient fire,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var tenant = Tenant(ctx);
            try
            {
                var req = new GetCasesRequest();
                if (!string.IsNullOrWhiteSpace(status) &&
                    Enum.TryParse<FireCaseStatus>(status, ignoreCase: true, out var parsed))
                    req.Status = parsed;

                var resp = await fire.GetCasesAsync(req, FireMeta(tenant, ct));
                return Results.Ok(resp.Cases);
            }
            catch (RpcException ex) { return MapRpcError(ex); }
        }).RequireAuthorization();

        app.MapGet("/api/fire/cases/{emergencyId}", async (
            string emergencyId,
            Fire.FireClient fire,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var tenant = Tenant(ctx);
            try
            {
                var resp = await fire.GetCaseAsync(
                    new GetCaseRequest { EmergencyId = emergencyId },
                    FireMeta(tenant, ct));
                return Results.Ok(resp);
            }
            catch (RpcException ex) { return MapRpcError(ex); }
        }).RequireAuthorization();

        app.MapPut("/api/fire/cases/{emergencyId}", async (
            string emergencyId,
            UpdateCaseBody body,
            Fire.FireClient fire,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var tenant = Tenant(ctx);
            try
            {
                if (!Enum.TryParse<FireCaseStatus>(body.Status, ignoreCase: true, out var status))
                    return Results.BadRequest($"Unknown status: {body.Status}");

                var req = new UpdateCaseRequest
                {
                    EmergencyId = emergencyId,
                    Status      = status
                };
                if (body.UnitId is not null) req.UnitId = body.UnitId;

                var resp = await fire.UpdateCaseAsync(req, FireMeta(tenant, ct));
                return Results.Ok(resp);
            }
            catch (RpcException ex) { return MapRpcError(ex); }
        }).RequireAuthorization();

        app.MapGet("/api/fire/units", async (
            Fire.FireClient fire,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var tenant = Tenant(ctx);
            try
            {
                var resp = await fire.GetUnitsAsync(new GetUnitsRequest(), FireMeta(tenant, ct));
                return Results.Ok(resp.Units);
            }
            catch (RpcException ex) { return MapRpcError(ex); }
        }).RequireAuthorization();

        app.MapPut("/api/fire/units/{unitId}/status", async (
            string unitId,
            UpdateUnitStatusBody body,
            Fire.FireClient fire,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var tenant = Tenant(ctx);
            try
            {
                if (!Enum.TryParse<FireUnitStatus>(body.Status, ignoreCase: true, out var status))
                    return Results.BadRequest($"Unknown status: {body.Status}");

                var resp = await fire.UpdateUnitStatusAsync(
                    new UpdateUnitStatusRequest { UnitId = unitId, Status = status },
                    FireMeta(tenant, ct));
                return Results.Ok(resp);
            }
            catch (RpcException ex) { return MapRpcError(ex); }
        }).RequireAuthorization();
    }

    private static TenantContext Tenant(HttpContext ctx) =>
        (TenantContext?)ctx.Items[TenantContextMiddleware.ItemsKey]
            ?? throw new InvalidOperationException("TenantContext missing.");

    private static CallOptions FireMeta(TenantContext tenant, CancellationToken ct)
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
        _                          => Results.Problem(ex.Status.Detail, statusCode: 500)
    };
}

record UpdateCaseBody(string Status, string? UnitId);
record UpdateUnitStatusBody(string Status);
