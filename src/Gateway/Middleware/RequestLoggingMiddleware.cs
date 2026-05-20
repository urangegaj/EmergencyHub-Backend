using System.Diagnostics;

namespace Gateway.Middleware;

public class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault()
            ?? Guid.NewGuid().ToString("N");

        context.Items["correlationId"] = correlationId;
        context.Response.Headers["X-Correlation-Id"] = correlationId;

        var sw = Stopwatch.StartNew();
        await next(context);
        sw.Stop();

        var tenant = (TenantContext?)context.Items[TenantContextMiddleware.ItemsKey];

        logger.LogInformation(
            "{Method} {Path} {StatusCode} {ElapsedMs}ms corr={CorrelationId} user={UserId} city={CityId}",
            context.Request.Method,
            context.Request.Path,
            context.Response.StatusCode,
            sw.ElapsedMilliseconds,
            correlationId,
            tenant?.UserId.ToString() ?? "-",
            tenant?.CityId.ToString() ?? "-");
    }
}
