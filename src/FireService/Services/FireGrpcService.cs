using FireService.Features.GetCases;
using FireService.Features.GetCase;
using FireService.Features.UpdateCase;
using FireService.Features.GetUnits;
using FireService.Features.UpdateUnitStatus;
using Grpc.Core;

namespace FireService.Services;

public class FireGrpcService(
    IGetCasesHandler getCases,
    IGetCaseHandler getCase,
    IUpdateCaseHandler updateCase,
    IGetUnitsHandler getUnits,
    IUpdateUnitStatusHandler updateUnitStatus) : Fire.FireBase
{
    public override Task<GetCasesResponse> GetCases(GetCasesRequest request, ServerCallContext context)
        => getCases.HandleAsync(request, context);

    public override Task<FireCaseResponse> GetCase(GetCaseRequest request, ServerCallContext context)
        => getCase.HandleAsync(request, context);

    public override Task<FireCaseResponse> UpdateCase(UpdateCaseRequest request, ServerCallContext context)
        => updateCase.HandleAsync(request, context);

    public override Task<GetUnitsResponse> GetUnits(GetUnitsRequest request, ServerCallContext context)
        => getUnits.HandleAsync(request, context);

    public override Task<FireUnitResponse> UpdateUnitStatus(UpdateUnitStatusRequest request, ServerCallContext context)
        => updateUnitStatus.HandleAsync(request, context);
}
