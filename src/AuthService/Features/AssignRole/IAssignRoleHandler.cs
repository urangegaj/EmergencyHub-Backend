using Grpc.Core;

namespace AuthService.Features.AssignRole;

public interface IAssignRoleHandler
{
    Task<AssignRoleResponse> HandleAsync(AssignRoleRequest request, ServerCallContext context);
}
