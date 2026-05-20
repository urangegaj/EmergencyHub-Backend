namespace Gateway.Middleware;

public record TenantContext(Guid UserId, Guid CityId, string Role, string? Department);
