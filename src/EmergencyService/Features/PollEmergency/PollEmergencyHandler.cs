using EmergencyService.Features.Shared;

using EmergencyService.Data;
using EmergencyService.Grpc;
using EmergencyService.Services;
using Grpc.Core;

namespace EmergencyService.Features.PollEmergency;

public class PollEmergencyHandler(EmergencyDbContext db, PollRegistry pollRegistry) : IPollEmergencyHandler
{
    public async Task<EmergencyResponse> HandleAsync(PollEmergencyRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.EmergencyId, out var emergencyId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid emergency_id."));
        if (!Guid.TryParse(request.CityId, out var cityId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid city_id."));

        var timeoutSeconds = request.TimeoutSeconds switch
        {
            <= 0 => 30,
            > 60 => 60,
            _ => request.TimeoutSeconds
        };

        var tcs = pollRegistry.Subscribe(emergencyId);
        try
        {
            var emergency = await EmergencyMapper.FetchAsync(db, emergencyId, cityId, context.CancellationToken);
            if (emergency.Version > request.Since)
                return EmergencyMapper.ToResponse(emergency);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                context.CancellationToken, timeoutCts.Token);

            try
            {
                await tcs.Task.WaitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException)
                when (timeoutCts.IsCancellationRequested && !context.CancellationToken.IsCancellationRequested)
            {
            }

            emergency = await EmergencyMapper.FetchAsync(db, emergencyId, cityId, context.CancellationToken);
            return EmergencyMapper.ToResponse(emergency);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw new RpcException(Status.DefaultCancelled);
        }
        finally
        {
            pollRegistry.Unsubscribe(emergencyId, tcs);
        }
    }
}
