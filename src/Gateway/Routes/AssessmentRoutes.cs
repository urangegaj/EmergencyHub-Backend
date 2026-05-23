using AssessmentService.Grpc;
using Gateway.Extensions;
using Grpc.Core;

namespace Gateway.Routes;

public static class AssessmentRoutes
{
    public static void MapAssessmentRoutes(this WebApplication app)
    {
        app.MapGet("/api/assessments/{emergencyId}", async (
            string emergencyId,
            Assessment.AssessmentClient assessment,
            CancellationToken ct) =>
        {
            if (ValidateEmergencyId(emergencyId) is { } idError)
                return idError;

            try
            {
                var resp = await assessment.GetReportAsync(
                    new GetReportRequest { EmergencyId = emergencyId },
                    ct.ToCallOptions());
                return Results.Ok(resp);
            }
            catch (RpcException ex) { return MapRpcError(ex); }
        }).RequireAuthorization();

        app.MapPost("/api/assessments/{emergencyId}/retry", async (
            string emergencyId,
            Assessment.AssessmentClient assessment,
            CancellationToken ct) =>
        {
            if (ValidateEmergencyId(emergencyId) is { } idError)
                return idError;

            try
            {
                var resp = await assessment.RetryReportAsync(
                    new RetryReportRequest { EmergencyId = emergencyId },
                    ct.ToCallOptions());
                return Results.Ok(resp);
            }
            catch (RpcException ex) { return MapRpcError(ex); }
        }).RequireAuthorization();
    }

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
