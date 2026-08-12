using Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public class TranscriptionBackgroundService : BackgroundService
{
    private static readonly TimeSpan MaxAttachmentTranscriptionDuration = TimeSpan.FromMinutes(10);

    private readonly ITranscriptionJobQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TranscriptionBackgroundService> _logger;

    public TranscriptionBackgroundService(
        ITranscriptionJobQueue queue,
        IServiceProvider serviceProvider,
        ILogger<TranscriptionBackgroundService> logger)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TranscriptionJob job;
            try
            {
                job = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await ProcessJobAsync(job, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process transcription job for attachment {AttachmentId}", job.AttachmentId);
            }
        }
    }

    private async Task ProcessJobAsync(TranscriptionJob job, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var transcriptionService = scope.ServiceProvider.GetRequiredService<ITranscriptionService>();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<IChatNotifier>();

        Application.Common.Result<TranscriptionResult> result;
        await using (var audioStream = await fileStorage.GetFileAsync(job.StoragePath, cancellationToken))
        {
            result = await transcriptionService.TranscribeAsync(
                audioStream, job.FileName, job.ContentType, MaxAttachmentTranscriptionDuration, cancellationToken);
        }

        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Value?.Text))
        {
            _logger.LogInformation("Transcription skipped for attachment {AttachmentId}: {Error}", job.AttachmentId, result.Error?.Code);
            return;
        }

        var attachment = await context.Attachments.FindAsync(new object[] { job.AttachmentId }, cancellationToken);
        if (attachment == null)
            return;

        attachment.TranscriptText = result.Value.Text;
        await context.SaveChangesAsync(cancellationToken);

        await notifier.AttachmentTranscribedAsync(job.RoomId, job.AttachmentId, result.Value.Text, cancellationToken);
    }
}
