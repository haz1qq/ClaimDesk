using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace NotificationsService;

/// <summary>
/// Reads claim-events and turns the ones a claimant cares about into notifications (DESIGN.md §8).
/// Delivery is at-least-once, so the same event can arrive twice. ProcessedEvents makes handling idempotent.
/// The offset is committed only AFTER the event is stored, so a crash means "process again", never "lose it".
/// </summary>
public sealed class ClaimEventsConsumer(
    IServiceScopeFactory scopes, IConfiguration config, TimeProvider clock, ILogger<ClaimEventsConsumer> log)
    : BackgroundService
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Consume() blocks a thread while it waits for messages, so the loop gets its own.
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => ConsumeLoopAsync(stoppingToken), stoppingToken);

    async Task ConsumeLoopAsync(CancellationToken ct)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = config["Kafka:BootstrapServers"],
            GroupId = "notifications",
            AutoOffsetReset = AutoOffsetReset.Earliest, // a brand-new group starts from the beginning
            EnableAutoCommit = false, // we commit ourselves, after the event is safely stored
        }).Build();
        consumer.Subscribe(config["Kafka:Topic"]);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                ConsumeResult<string, string> result;
                try
                {
                    result = consumer.Consume(ct);
                }
                catch (ConsumeException ex)
                {
                    log.LogWarning("Kafka consume error ({Reason}); retrying", ex.Error.Reason);
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                    continue;
                }

                try
                {
                    await HandleAsync(result.Message.Value, ct);
                    consumer.Commit(result);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Only transient failures get here (e.g. the database is down): bad messages are skipped in
                    // HandleAsync. Don't skip a good event because of an outage: wait and read it again.
                    log.LogError(ex, "Failed to handle event at {Offset}; retrying in 5s", result.TopicPartitionOffset);
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                    consumer.Seek(result.TopicPartitionOffset);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        finally
        {
            consumer.Close(); // leave the group cleanly so partitions are reassigned quickly
        }
    }

    async Task HandleAsync(string value, CancellationToken ct)
    {
        ClaimEventMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<ClaimEventMessage>(value, Json);
        }
        catch (JsonException ex)
        {
            // A message we can never read shouldn't block everything behind it: log it and move on.
            log.LogError(ex, "Skipping malformed claim event");
            return;
        }
        if (message is null || !message.IsValid)
        {
            log.LogError("Skipping claim event with missing or invalid fields");
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        if (await db.ProcessedEvents.AnyAsync(p => p.EventId == message.EventId, ct))
            return; // duplicate delivery: already handled

        var now = clock.GetUtcNow();
        var text = NotificationText.For(message);
        var notificationId = Guid.NewGuid();
        db.ProcessedEvents.Add(new ProcessedEvent { EventId = message.EventId, ProcessedAt = now });
        if (text is not null)
        {
            db.Notifications.Add(new Notification
            {
                Id = notificationId,
                RecipientId = message.ClaimantId,
                ClaimId = message.ClaimId,
                ClaimNumber = message.ClaimNumber,
                Message = text,
                CreatedAt = now,
            });
        }

        try
        {
            await db.SaveChangesAsync(ct); // marker and notification are saved in one transaction
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return; // another delivery of the same event won the race: already handled
        }

        if (text is not null) // ids only: the message text can contain claim details
            log.LogInformation("Email sent (simulated): notification {NotificationId} for {EventType} to {RecipientId}",
                notificationId, message.Type, message.ClaimantId);
    }
}
