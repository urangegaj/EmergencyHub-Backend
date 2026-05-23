using AuthService;
using Grpc.Core;

namespace NotificationService.Services;

public sealed class AuthUserLookupService(
    Auth.AuthClient auth,
    ILogger<AuthUserLookupService> logger)
{
    public async Task<UserResponse?> GetUserAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            return await auth.GetUserAsync(
                new GetUserRequest { UserId = userId.ToString() },
                cancellationToken: ct);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            logger.LogWarning("User {UserId} not found in AuthService", userId);
            return null;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
        {
            logger.LogWarning("Invalid user id {UserId}: {Detail}", userId, ex.Status.Detail);
            return null;
        }
        catch (RpcException ex)
        {
            logger.LogError(ex, "AuthService unavailable while looking up user {UserId}", userId);
            throw;
        }
    }

    public async Task<IReadOnlyList<UserResponse>> ListDepartmentUsersAsync(
        Guid cityId,
        string department,
        CancellationToken ct)
    {
        try
        {
            var response = await auth.ListDepartmentUsersAsync(
                new ListDepartmentUsersRequest
                {
                    CityId = cityId.ToString(),
                    Department = department
                },
                cancellationToken: ct);

            return response.Users;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
        {
            logger.LogWarning("Invalid department lookup city={CityId} department={Department}: {Detail}",
                cityId, department, ex.Status.Detail);
            return [];
        }
        catch (RpcException ex)
        {
            logger.LogError(ex,
                "AuthService unavailable while listing department users city={CityId} department={Department}",
                cityId, department);
            throw;
        }
    }
}
