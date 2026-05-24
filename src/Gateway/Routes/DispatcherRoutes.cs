using FireService;
using MedicalService;
using PoliceService;
using Gateway.Middleware;
using Grpc.Core;

namespace Gateway.Routes;

public static class DispatcherRoutes
{
    public static void MapDispatcherRoutes(this WebApplication app)
    {
        app.MapGet("/api/dispatcher/units", async (
            Police.PoliceClient police,
            Fire.FireClient fire,
            Medical.MedicalClient medical,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var tenant = Tenant(ctx);
            try
            {
                var policeTask = police.GetUnitsAsync(new PoliceService.GetUnitsRequest(), PoliceMeta(tenant, ct)).ResponseAsync;
                var fireTask   = fire.GetUnitsAsync(new FireService.GetUnitsRequest(), FireMeta(tenant, ct)).ResponseAsync;
                var medTask    = medical.GetUnitsAsync(new MedicalService.GetUnitsRequest(), MedicalMeta(tenant, ct)).ResponseAsync;

                await Task.WhenAll(policeTask, fireTask, medTask);

                return Results.Ok(new
                {
                    police  = policeTask.Result.Units,
                    fire    = fireTask.Result.Units,
                    medical = medTask.Result.Units
                });
            }
            catch (RpcException ex) { return MapRpcError(ex); }
        }).RequireAuthorization();
    }

    private static TenantContext Tenant(HttpContext ctx) =>
        (TenantContext?)ctx.Items[TenantContextMiddleware.ItemsKey]
            ?? throw new InvalidOperationException("TenantContext missing.");

    private static CallOptions PoliceMeta(TenantContext tenant, CancellationToken ct) =>
        new(headers: new Metadata { { "city_id", tenant.CityId.ToString() } }, cancellationToken: ct);

    private static CallOptions FireMeta(TenantContext tenant, CancellationToken ct) =>
        new(headers: new Metadata { { "city_id", tenant.CityId.ToString() } }, cancellationToken: ct);

    private static CallOptions MedicalMeta(TenantContext tenant, CancellationToken ct) =>
        new(headers: new Metadata { { "city_id", tenant.CityId.ToString() } }, cancellationToken: ct);

    private static IResult MapRpcError(RpcException ex) => ex.StatusCode switch
    {
        StatusCode.NotFound        => Results.NotFound(ex.Status.Detail),
        StatusCode.InvalidArgument => Results.BadRequest(ex.Status.Detail),
        StatusCode.Unauthenticated => Results.Unauthorized(),
        StatusCode.Unavailable     => Results.StatusCode(503),
        _                          => Results.Problem(ex.Status.Detail, statusCode: 500)
    };
}
