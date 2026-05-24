using System.Security.Cryptography;
using AuthService.Models;
using Grpc.Core;

namespace AuthService.Features.Shared;

internal static class AuthMapper
{
    internal static readonly TimeSpan RefreshTokenTtl = TimeSpan.FromDays(7);

    internal static string RefreshKey(string token) => $"refresh:{token}";

    internal static string GenerateRefreshToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    internal static Guid ExtractCityId(ServerCallContext context)
    {
        var raw = context.RequestHeaders.GetValue("city_id")
            ?? throw new RpcException(new Status(StatusCode.InvalidArgument, "city_id metadata missing."));
        return Guid.TryParse(raw, out var id) ? id
            : throw new RpcException(new Status(StatusCode.InvalidArgument, "city_id is not a valid GUID."));
    }

    internal static UserResponse MapUser(User user)
    {
        var response = new UserResponse
        {
            UserId = user.Id.ToString(),
            Email = user.Email,
            Role = user.Role.Name
        };

        if (user.Department.HasValue)
            response.Department = user.Department.Value.ToString();

        return response;
    }

    internal static AdminUserResponse MapAdminUser(User user)
    {
        var r = new AdminUserResponse
        {
            UserId = user.Id.ToString(),
            Email = user.Email,
            Role = user.Role.Name,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt.ToString("O"),
            FirstName = user.Profile?.FirstName ?? "",
            LastName = user.Profile?.LastName ?? ""
        };
        if (user.Department.HasValue) r.Department = user.Department.Value.ToString();
        if (user.Profile?.Phone is not null) r.Phone = user.Profile.Phone;
        return r;
    }
}
