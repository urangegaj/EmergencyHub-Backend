using AuthService.Features.Shared;

using AuthService.Data;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Shared.Enums;

namespace AuthService.Features.ListUsers;

public class ListUsersHandler(AuthDbContext db) : IListUsersHandler
{
    public async Task<PagedUsersResponse> HandleAsync(ListUsersRequest request, ServerCallContext context)
    {
        var cityId = AuthMapper.ExtractCityId(context);

        var query = db.Users
            .Include(u => u.Role)
            .Include(u => u.Profile)
            .Where(u => u.CityId == cityId);

        if (!string.IsNullOrEmpty(request.Role))
            query = query.Where(u => u.Role.Name == request.Role);

        if (!string.IsNullOrEmpty(request.Department)
            && Enum.TryParse<DepartmentType>(request.Department, ignoreCase: true, out var dept))
            query = query.Where(u => u.Department == dept);

        var total = await query.CountAsync(context.CancellationToken);

        var page = request.Page > 0 ? request.Page : 1;
        var pageSize = request.PageSize > 0 ? request.PageSize : 20;

        var users = await query
            .OrderBy(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(context.CancellationToken);

        var response = new PagedUsersResponse
        {
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
        response.Users.AddRange(users.Select(AuthMapper.MapAdminUser));
        return response;
    }
}
