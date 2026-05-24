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
            var userId = Guid.Parse(claims.First(c => c.Type == ClaimNames.UserId).Value);
            var cityId = Guid.Parse(claims.First(c => c.Type == ClaimNames.CityId).Value);
            var role = claims.First(c => c.Type == ClaimNames.Role).Value;
            var department = claims.FirstOrDefault(c => c.Type == ClaimNames.Department)?.Value;
            var firstName = claims.FirstOrDefault(c => c.Type == ClaimNames.FirstName)?.Value;
            var lastName = claims.FirstOrDefault(c => c.Type == ClaimNames.LastName)?.Value;
            var phone = claims.FirstOrDefault(c => c.Type == ClaimNames.Phone)?.Value;

            context.Items[ItemsKey] = new TenantContext(userId, cityId, role, department, firstName, lastName, phone);
        }

        await next(context);
    }
}
