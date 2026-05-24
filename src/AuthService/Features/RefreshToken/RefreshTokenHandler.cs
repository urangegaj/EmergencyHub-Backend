using AuthService.Features.Shared;

using AuthService.Data;
using AuthService.Services;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace AuthService.Features.RefreshToken;

public class RefreshTokenHandler(
    AuthDbContext db,
    IConnectionMultiplexer redis,
    TokenService tokens) : IRefreshTokenHandler
{
    private readonly IDatabase _cache = redis.GetDatabase();

    public async Task<LoginResponse> HandleAsync(RefreshRequest request, ServerCallContext context)
    {
        var key = AuthMapper.RefreshKey(request.RefreshToken);
        var stored = await _cache.StringGetAsync(key);

        if (!stored.HasValue || !Guid.TryParse(stored, out var userId))
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid or expired refresh token."));

        await _cache.KeyDeleteAsync(key);

        var user = await db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "User not found."));

        var newRefreshToken = AuthMapper.GenerateRefreshToken();
        await _cache.StringSetAsync(AuthMapper.RefreshKey(newRefreshToken), user.Id.ToString(), AuthMapper.RefreshTokenTtl);

        var response = new LoginResponse
        {
            AccessToken = tokens.IssueAccessToken(user),
            RefreshToken = newRefreshToken,
            UserId = user.Id.ToString(),
            CityId = user.CityId.ToString(),
            Role = user.Role.Name
        };

        if (user.Department.HasValue)
            response.Department = user.Department.Value.ToString();

        return response;
    }
}
