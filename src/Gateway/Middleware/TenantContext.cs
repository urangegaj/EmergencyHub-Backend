namespace Gateway.Middleware;

public record TenantContext(int UserId, int CityId, string Role, string? Department);
