using AuthService.Features.Shared;

using AuthService.Data;
using AuthService.Services;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace AuthService.Features.Login;

public class LoginHandler(
    AuthDbContext db,
    IConnectionMultiplexer redis,
    TokenService tokens) : ILoginHandler
{
    private readonly IDatabase _cache = redis.GetDatabase();

    public async Task<LoginResponse> HandleAsync(LoginRequest request, ServerCallContext context)
    {
        var user = await db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Email == request.Email)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "Invalid credentials."));

        if (!user.IsActive)
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Account is deactivated."));

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid credentials."));

        var refreshToken = AuthMapper.GenerateRefreshToken();
        await _cache.StringSetAsync(AuthMapper.RefreshKey(refreshToken), user.Id.ToString(), AuthMapper.RefreshTokenTtl);

        var response = new LoginResponse
        {
            AccessToken = tokens.IssueAccessToken(user),
            RefreshToken = refreshToken,
            UserId = user.Id.ToString(),
            CityId = user.CityId.ToString(),
            Role = user.Role.Name
        };

        if (user.Department.HasValue)
            response.Department = user.Department.Value.ToString();

        return response;
    }
}
