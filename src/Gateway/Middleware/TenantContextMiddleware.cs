using Shared.Auth;

namespace Gateway.Middleware;

public class TenantContextMiddleware(RequestDelegate next)
{
    public const string ItemsKey = "tenant";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var claims = context.User.Claims;
            var userId = int.Parse(claims.First(c => c.Type == ClaimNames.UserId).Value);
            var cityId = int.Parse(claims.First(c => c.Type == ClaimNames.CityId).Value);
            var role = claims.First(c => c.Type == ClaimNames.Role).Value;
            var department = claims.FirstOrDefault(c => c.Type == ClaimNames.Department)?.Value;

            context.Items[ItemsKey] = new TenantContext(userId, cityId, role, department);
        }

        await next(context);
    }
}
