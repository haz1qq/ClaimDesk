using System.Text.Json;
using System.Text.Json.Serialization;
using ClaimsApi.Data;
using ClaimsApi.Domain;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClaimsApi.Messaging;

public sealed class KafkaOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string Topic { get; set; } = "claim-events";
}

/// <summary>
/// The transactional outbox, publishing half (DESIGN.md §8).
/// Claim changes write a ClaimEvent row in the SAME transaction as the claim, so an event can't be lost.
/// This loop sends unpublished rows to Kafka in order, then sets PublishedAt.
/// If the process dies after sending but before saving PublishedAt, the event is sent again:
/// delivery is at-least-once, and consumers de-duplicate on eventId.
/// </summary>
public sealed class OutboxPublisher(
    IServiceScopeFactory scopes, IOptions<KafkaOptions> options, TimeProvider clock, ILogger<OutboxPublisher> log)
    : BackgroundService
{
    static readonly JsonSerializerOptions MessageJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var kafka = options.Value;
        await EnsureTopicAsync(kafka, stoppingToken);

        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            EnableIdempotence = true, // the broker drops duplicates caused by the producer's own retries
            Acks = Acks.All,
            MessageTimeoutMs = 30_000, // give up on a send after 30s; the row stays unpublished and is retried
        }).Build();

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await PublishBatchAsync(producer, kafka.Topic, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Outbox publish failed; retrying on the next tick");
            }
        }
    }

    async Task PublishBatchAsync(IProducer<string, string> producer, string topic, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();

        var pending = await db.ClaimEvents
            .Where(e => e.PublishedAt == null)
            .Join(db.Claims, e => e.ClaimId, c => c.Id, (e, c) => new { Event = e, c.Number, c.ClaimantId })
            .OrderBy(x => x.Event.OccurredAt)
            .Take(100)
            .ToListAsync(ct);

        foreach (var item in pending)
        {
            var e = item.Event;
            var message = new
            {
                eventId = e.Id,
                type = e.Type,
                claimId = e.ClaimId,
                claimNumber = Claim.FormatNumber(item.Number),
                claimantId = item.ClaimantId,
                occurredAt = e.OccurredAt,
                data = JsonDocument.Parse(e.Payload).RootElement,
            };

            try
            {
                // Key = claim id: all events for one claim go to the same partition, so they stay in order.
                await producer.ProduceAsync(topic,
                    new Message<string, string> { Key = e.ClaimId.ToString(), Value = JsonSerializer.Serialize(message, MessageJson) },
                    ct);
            }
            catch (ProduceException<string, string> ex)
            {
                log.LogWarning("Kafka send failed ({Reason}); stopping this batch", ex.Error.Reason);
                break; // keep order: don't send later events before this one
            }

            e.MarkPublished(clock.GetUtcNow());
        }

        await db.SaveChangesAsync(ct);
    }

    async Task EnsureTopicAsync(KafkaOptions kafka, CancellationToken ct)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = kafka.BootstrapServers }).Build();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Created explicitly: broker auto-creation would give the topic a single partition.
                await admin.CreateTopicsAsync([new TopicSpecification { Name = kafka.Topic, NumPartitions = 3, ReplicationFactor = 1 }]);
                log.LogInformation("Created Kafka topic {Topic}", kafka.Topic);
                return;
            }
            catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
            {
                return;
            }
            catch (KafkaException ex)
            {
                log.LogWarning("Kafka not reachable yet ({Reason}); retrying in 5s", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }
}
