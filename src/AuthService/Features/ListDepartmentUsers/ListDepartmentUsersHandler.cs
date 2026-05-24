using AuthService.Features.Shared;

using AuthService.Data;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Shared.Enums;

namespace AuthService.Features.ListDepartmentUsers;

public class ListDepartmentUsersHandler(AuthDbContext db) : IListDepartmentUsersHandler
{
    public async Task<ListUsersResponse> HandleAsync(ListDepartmentUsersRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.CityId, out var cityId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid city_id."));

        if (!Enum.TryParse<DepartmentType>(request.Department, ignoreCase: true, out var department))
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"Unknown department: {request.Department}."));

        var users = await db.Users
            .Include(u => u.Role)
            .Where(u => u.CityId == cityId
                        && u.Department == department
                        && u.Role.Name == nameof(UserRole.Responder))
            .ToListAsync(context.CancellationToken);

        var response = new ListUsersResponse();
        response.Users.AddRange(users.Select(AuthMapper.MapUser));
        return response;
    }
}
