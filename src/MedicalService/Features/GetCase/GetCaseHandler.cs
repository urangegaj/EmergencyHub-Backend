using MedicalService.Features.Shared;

using Grpc.Core;
using MedicalService.Data;
using Microsoft.EntityFrameworkCore;

namespace MedicalService.Features.GetCase;

public class GetCaseHandler(MedicalDbContext db) : IGetCaseHandler
{
    public async Task<MedicalCaseResponse> HandleAsync(GetCaseRequest request, ServerCallContext context)
    {
        var cityId = MedicalMapper.GetCityId(context);

        if (!Guid.TryParse(request.EmergencyId, out var emergencyId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid emergency_id."));

        var medicalCase = await db.Cases
            .FirstOrDefaultAsync(c => c.EmergencyId == emergencyId && c.CityId == cityId,
                context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "Medical case not found."));

        Models.MedicalUnit? unit = null;
        if (medicalCase.AssignedUnitId.HasValue)
            unit = await db.Units.FindAsync([medicalCase.AssignedUnitId.Value], context.CancellationToken);

        return MedicalMapper.MapCase(medicalCase, unit);
    }
}
