using Grpc.Core;

namespace AuthService.Features.ListDepartmentUsers;

public interface IListDepartmentUsersHandler
{
    Task<ListUsersResponse> HandleAsync(ListDepartmentUsersRequest request, ServerCallContext context);
}
