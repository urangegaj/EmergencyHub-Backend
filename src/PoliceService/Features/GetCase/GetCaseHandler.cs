using PoliceService.Features.Shared;

using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using PoliceService.Data;

namespace PoliceService.Features.GetCase;

public class GetCaseHandler(PoliceDbContext db) : IGetCaseHandler
{
    public async Task<PoliceCaseResponse> HandleAsync(GetCaseRequest request, ServerCallContext context)
    {
        var cityId = PoliceMapper.GetCityId(context);

        if (!Guid.TryParse(request.EmergencyId, out var emergencyId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid emergency_id."));

        var policeCase = await db.Cases
            .FirstOrDefaultAsync(c => c.EmergencyId == emergencyId && c.CityId == cityId,
                context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "Police case not found."));

        Models.PoliceUnit? unit = null;
        if (policeCase.AssignedUnitId.HasValue)
            unit = await db.Units.FindAsync([policeCase.AssignedUnitId.Value], context.CancellationToken);

        return PoliceMapper.MapCase(policeCase, unit);
    }
}
