using Microsoft.EntityFrameworkCore;
using NotificationService.Data;
using NotificationService.Models;
using NotificationService.Services;

namespace NotificationService.Services;

public sealed class RetryFailedJobsWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<RetryFailedJobsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
                var dispatch = scope.ServiceProvider.GetRequiredService<NotificationDispatchService>();

                var now = DateTime.UtcNow;
                var jobs = await db.BackgroundJobs
                    .Where(j => j.Type == BackgroundJobTypes.EmailSend
                                && j.Status == BackgroundJobStatus.PENDING
                                && j.NextRetryAt != null
                                && j.NextRetryAt <= now)
                    .OrderBy(j => j.NextRetryAt)
                    .Take(20)
                    .ToListAsync(stoppingToken);

                foreach (var job in jobs)
                    await dispatch.RetryEmailJobAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in retry worker loop");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
