using Grpc.Core;
using MedicalService.Features.GetCases;
using MedicalService.Features.GetCase;
using MedicalService.Features.UpdateCase;
using MedicalService.Features.GetUnits;
using MedicalService.Features.UpdateUnitStatus;

namespace MedicalService.Services;

public class MedicalGrpcService(
    IGetCasesHandler getCases,
    IGetCaseHandler getCase,
    IUpdateCaseHandler updateCase,
    IGetUnitsHandler getUnits,
    IUpdateUnitStatusHandler updateUnitStatus) : Medical.MedicalBase
{
    public override Task<GetCasesResponse> GetCases(GetCasesRequest request, ServerCallContext context)
        => getCases.HandleAsync(request, context);

    public override Task<MedicalCaseResponse> GetCase(GetCaseRequest request, ServerCallContext context)
        => getCase.HandleAsync(request, context);

    public override Task<MedicalCaseResponse> UpdateCase(UpdateCaseRequest request, ServerCallContext context)
        => updateCase.HandleAsync(request, context);

    public override Task<GetUnitsResponse> GetUnits(GetUnitsRequest request, ServerCallContext context)
        => getUnits.HandleAsync(request, context);

    public override Task<MedicalUnitResponse> UpdateUnitStatus(UpdateUnitStatusRequest request, ServerCallContext context)
        => updateUnitStatus.HandleAsync(request, context);
}
