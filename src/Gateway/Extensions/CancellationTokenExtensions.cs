using Grpc.Core;

namespace Gateway.Extensions;

public static class CancellationTokenExtensions
{
    public static CallOptions ToCallOptions(this CancellationToken ct)
        => new(cancellationToken: ct);
}
