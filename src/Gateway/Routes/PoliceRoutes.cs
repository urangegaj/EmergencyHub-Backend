using PoliceService;
using Gateway.Extensions;
using Gateway.Middleware;
using Grpc.Core;

namespace Gateway.Routes;

public static class PoliceRoutes
{
    public static void MapPoliceRoutes(this WebApplication app)
    {
        app.MapGet("/api/police/cases", async (
            string? status,
            Police.PoliceClient police,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var tenant = Tenant(ctx);
            if (!CanReadPolice(tenant)) return Results.Forbid();

            try
            {
                var req = new GetCasesRequest();
                if (!string.IsNullOrWhiteSpace(status) &&
                    ProtoEnumParse.TryParse<PoliceCaseStatus>(status, out var parsed))
                    req.Status = parsed;

                var resp = await police.GetCasesAsync(req, PoliceMeta(tenant, ct));
                return Results.Ok(resp.Cases);
            }
            catch (RpcException ex) { return MapRpcError(ex); }
        }).RequireAuthorization();

        app.MapGet("/api/police/cases/{emergencyId}", async (
            string emergencyId,
            Police.PoliceClient police,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var tenant = Tenant(ctx);
            if (!CanReadPolice(tenant)) return Results.Forbid();

            try
            {
                var resp = await police.GetCaseAsync(
                    new GetCaseRequest { EmergencyId = emergencyId },
                    PoliceMeta(tenant, ct));
                return Results.Ok(resp);
            }
            catch (RpcException ex) { return MapRpcError(ex); }
        }).RequireAuthorization();

        app.MapPut("/api/police/cases/{emergencyId}", async (
            string emergencyId,
            UpdatePoliceCaseBody body,
            Police.PoliceClient police,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var tenant = Tenant(ctx);
            if (!CanWritePolice(tenant)) return Results.Forbid();

            try
            {
                if (!ProtoEnumParse.TryParse<PoliceCaseStatus>(body.Status, out var status))
                    return Results.BadRequest($"Unknown status: {body.Status}");

                var req = new UpdateCaseRequest
                {
                    EmergencyId = emergencyId,
                    Status = status
                };
                if (body.UnitId is not null) req.UnitId = body.UnitId;

                var resp = await police.UpdateCaseAsync(req, PoliceMeta(tenant, ct));
                return Results.Ok(resp);
            }
            catch (RpcException ex) { return MapRpcError(ex); }
        }).RequireAuthorization();

        app.MapGet("/api/police/units", async (
            Police.PoliceClient police,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var tenant = Tenant(ctx);
            if (!CanReadPolice(tenant)) return Results.Forbid();

            try
            {
                var resp = await police.GetUnitsAsync(new GetUnitsRequest(), PoliceMeta(tenant, ct));
                return Results.Ok(resp.Units);
            }
            catch (RpcException ex) { return MapRpcError(ex); }
        }).RequireAuthorization();

        app.MapPut("/api/police/units/{unitId}/status", async (
            string unitId,
            UpdatePoliceUnitStatusBody body,
            Police.PoliceClient police,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var tenant = Tenant(ctx);
            if (!CanWritePolice(tenant)) return Results.Forbid();

            try
            {
                if (!ProtoEnumParse.TryParse<PoliceUnitStatus>(body.Status, out var status))
                    return Results.BadRequest($"Unknown status: {body.Status}");

                var resp = await police.UpdateUnitStatusAsync(
                    new UpdateUnitStatusRequest { UnitId = unitId, Status = status },
                    PoliceMeta(tenant, ct));
                return Results.Ok(resp);
            }
            catch (RpcException ex) { return MapRpcError(ex); }
        }).RequireAuthorization();
    }


    private static bool CanReadPolice(TenantContext t) =>
        t.Role == "Admin" || t.Role == "Dispatcher" || (t.Role == "Responder" && t.Department == "Police");

    private static bool CanWritePolice(TenantContext t) =>
        t.Role == "Admin" || (t.Role == "Responder" && t.Department == "Police");

    private static TenantContext Tenant(HttpContext ctx) =>
        (TenantContext?)ctx.Items[TenantContextMiddleware.ItemsKey]
            ?? throw new InvalidOperationException("TenantContext missing.");

    private static CallOptions PoliceMeta(TenantContext tenant, CancellationToken ct)
    {
        var meta = new Metadata { { "city_id", tenant.CityId.ToString() } };
        return new CallOptions(headers: meta, cancellationToken: ct);
    }

    private static IResult MapRpcError(RpcException ex) => ex.StatusCode switch
    {
        StatusCode.NotFound => Results.NotFound(ex.Status.Detail),
        StatusCode.InvalidArgument => Results.BadRequest(ex.Status.Detail),
        StatusCode.Unauthenticated => Results.Unauthorized(),
        StatusCode.AlreadyExists => Results.Conflict(ex.Status.Detail),
        StatusCode.Unavailable => Results.StatusCode(503),
        _ => Results.Problem(ex.Status.Detail, statusCode: 500)
    };
}

record UpdatePoliceCaseBody(string Status, string? UnitId);
record UpdatePoliceUnitStatusBody(string Status);
