using AuthService.Features.Register;
using AuthService.Features.Login;
using AuthService.Features.RefreshToken;
using AuthService.Features.Logout;
using AuthService.Features.GetUser;
using AuthService.Features.ListDepartmentUsers;
using AuthService.Features.ListUsers;
using AuthService.Features.CreateUser;
using AuthService.Features.UpdateUser;
using AuthService.Features.DeactivateUser;
using AuthService.Features.AssignRole;
using Grpc.Core;

namespace AuthService.Services;

public class AuthGrpcService(
    IRegisterHandler register,
    ILoginHandler login,
    IRefreshTokenHandler refreshToken,
    ILogoutHandler logout,
    IGetUserHandler getUser,
    IListDepartmentUsersHandler listDepartmentUsers,
    IListUsersHandler listUsers,
    ICreateUserHandler createUser,
    IUpdateUserHandler updateUser,
    IDeactivateUserHandler deactivateUser,
    IAssignRoleHandler assignRole) : Auth.AuthBase
{
    public override Task<RegisterResponse> Register(RegisterRequest request, ServerCallContext context)
        => register.HandleAsync(request, context);

    public override Task<LoginResponse> Login(LoginRequest request, ServerCallContext context)
        => login.HandleAsync(request, context);

    public override Task<LoginResponse> Refresh(RefreshRequest request, ServerCallContext context)
        => refreshToken.HandleAsync(request, context);

    public override Task<LogoutResponse> Logout(LogoutRequest request, ServerCallContext context)
        => logout.HandleAsync(request, context);

    public override Task<UserResponse> GetUser(GetUserRequest request, ServerCallContext context)
        => getUser.HandleAsync(request, context);

    public override Task<ListUsersResponse> ListDepartmentUsers(ListDepartmentUsersRequest request, ServerCallContext context)
        => listDepartmentUsers.HandleAsync(request, context);

    public override Task<PagedUsersResponse> ListUsers(ListUsersRequest request, ServerCallContext context)
        => listUsers.HandleAsync(request, context);

    public override Task<CreateUserResponse> CreateUser(CreateUserRequest request, ServerCallContext context)
        => createUser.HandleAsync(request, context);

    public override Task<UpdateUserResponse> UpdateUser(UpdateUserRequest request, ServerCallContext context)
        => updateUser.HandleAsync(request, context);

    public override Task<DeactivateUserResponse> DeactivateUser(DeactivateUserRequest request, ServerCallContext context)
        => deactivateUser.HandleAsync(request, context);

    public override Task<AssignRoleResponse> AssignRole(AssignRoleRequest request, ServerCallContext context)
        => assignRole.HandleAsync(request, context);
}
