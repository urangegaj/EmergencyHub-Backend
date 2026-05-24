using Grpc.Core;
using PoliceService.Features.GetCases;
using PoliceService.Features.GetCase;
using PoliceService.Features.UpdateCase;
using PoliceService.Features.GetUnits;
using PoliceService.Features.UpdateUnitStatus;

namespace PoliceService.Services;

public class PoliceGrpcService(
    IGetCasesHandler getCases,
    IGetCaseHandler getCase,
    IUpdateCaseHandler updateCase,
    IGetUnitsHandler getUnits,
    IUpdateUnitStatusHandler updateUnitStatus) : Police.PoliceBase
{
    public override Task<GetCasesResponse> GetCases(GetCasesRequest request, ServerCallContext context)
        => getCases.HandleAsync(request, context);

    public override Task<PoliceCaseResponse> GetCase(GetCaseRequest request, ServerCallContext context)
        => getCase.HandleAsync(request, context);

    public override Task<PoliceCaseResponse> UpdateCase(UpdateCaseRequest request, ServerCallContext context)
        => updateCase.HandleAsync(request, context);

    public override Task<GetUnitsResponse> GetUnits(GetUnitsRequest request, ServerCallContext context)
        => getUnits.HandleAsync(request, context);

    public override Task<PoliceUnitResponse> UpdateUnitStatus(UpdateUnitStatusRequest request, ServerCallContext context)
        => updateUnitStatus.HandleAsync(request, context);
}
