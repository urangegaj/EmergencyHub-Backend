using Grpc.Core;

namespace AuthService.Features.ListUsers;

public interface IListUsersHandler
{
    Task<PagedUsersResponse> HandleAsync(ListUsersRequest request, ServerCallContext context);
}
