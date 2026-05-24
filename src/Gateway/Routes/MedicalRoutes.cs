using MedicalService;
using Gateway.Extensions;
using Gateway.Middleware;
using Grpc.Core;

namespace Gateway.Routes;

public static class MedicalRoutes
{
    public static void MapMedicalRoutes(this WebApplication app)
    {
        app.MapGet("/api/medical/cases", async (
            string? status,
            Medical.MedicalClient medical,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var tenant = Tenant(ctx);
            if (!CanReadMedical(tenant)) return Results.Forbid();

            try
            {
                var req = new GetCasesRequest();
                if (!string.IsNullOrWhiteSpace(status) &&
                    ProtoEnumParse.TryParse<MedicalCaseStatus>(status, out var parsed))
                    req.Status = parsed;

                var resp = await medical.GetCasesAsync(req, MedicalMeta(tenant, ct));
                return Results.Ok(resp.Cases);
            }
            catch (RpcException ex) { return MapRpcError(ex); }
        }).RequireAuthorization();

        app.MapGet("/api/medical/cases/{emergencyId}", async (
            string emergencyId,
            Medical.MedicalClient medical,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var tenant = Tenant(ctx);
            if (!CanReadMedical(tenant)) return Results.Forbid();

            try
            {
                var resp = await medical.GetCaseAsync(
                    new GetCaseRequest { EmergencyId = emergencyId },
                    MedicalMeta(tenant, ct));
                return Results.Ok(resp);
            }
            catch (RpcException ex) { return MapRpcError(ex); }
        }).RequireAuthorization();

        app.MapPut("/api/medical/cases/{emergencyId}", async (
            string emergencyId,
            UpdateMedicalCaseBody body,
            Medical.MedicalClient medical,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var tenant = Tenant(ctx);
            if (!CanWriteMedical(tenant)) return Results.Forbid();

            try
            {
                if (!ProtoEnumParse.TryParse<MedicalCaseStatus>(body.Status, out var status))
                    return Results.BadRequest($"Unknown status: {body.Status}");

                var req = new UpdateCaseRequest
                {
                    EmergencyId = emergencyId,
                    Status = status
                };
                if (body.UnitId is not null) req.UnitId = body.UnitId;

                var resp = await medical.UpdateCaseAsync(req, MedicalMeta(tenant, ct));
                return Results.Ok(resp);
            }
            catch (RpcException ex) { return MapRpcError(ex); }
        }).RequireAuthorization();

        app.MapGet("/api/medical/units", async (
            Medical.MedicalClient medical,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var tenant = Tenant(ctx);
            if (!CanReadMedical(tenant)) return Results.Forbid();

            try
            {
                var resp = await medical.GetUnitsAsync(new GetUnitsRequest(), MedicalMeta(tenant, ct));
                return Results.Ok(resp.Units);
            }
            catch (RpcException ex) { return MapRpcError(ex); }
        }).RequireAuthorization();

        app.MapPut("/api/medical/units/{unitId}/status", async (
            string unitId,
            UpdateMedicalUnitStatusBody body,
            Medical.MedicalClient medical,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var tenant = Tenant(ctx);
            if (!CanWriteMedical(tenant)) return Results.Forbid();

            try
            {
                if (!ProtoEnumParse.TryParse<MedicalUnitStatus>(body.Status, out var status))
                    return Results.BadRequest($"Unknown status: {body.Status}");

                var resp = await medical.UpdateUnitStatusAsync(
                    new UpdateUnitStatusRequest { UnitId = unitId, Status = status },
                    MedicalMeta(tenant, ct));
                return Results.Ok(resp);
            }
            catch (RpcException ex) { return MapRpcError(ex); }
        }).RequireAuthorization();
    }


    private static bool CanReadMedical(TenantContext t) =>
        t.Role == "Admin" || t.Role == "Dispatcher" || (t.Role == "Responder" && t.Department == "Medical");

    private static bool CanWriteMedical(TenantContext t) =>
        t.Role == "Admin" || (t.Role == "Responder" && t.Department == "Medical");

    private static TenantContext Tenant(HttpContext ctx) =>
        (TenantContext?)ctx.Items[TenantContextMiddleware.ItemsKey]
            ?? throw new InvalidOperationException("TenantContext missing.");

    private static CallOptions MedicalMeta(TenantContext tenant, CancellationToken ct)
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

record UpdateMedicalCaseBody(string Status, string? UnitId);
record UpdateMedicalUnitStatusBody(string Status);
