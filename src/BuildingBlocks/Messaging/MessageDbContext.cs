using Microsoft.EntityFrameworkCore;

namespace OrderProcessing.Messaging;

// These are local tables in EACH service database, never a shared persistence store.
public abstract class MessageDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<InboxMessage> Inbox => Set<InboxMessage>();

    public void Enqueue(EventEnvelope envelope) => Outbox.Add(new OutboxMessage
    {
        Id = envelope.MessageId,
        Payload = System.Text.Json.JsonSerializer.Serialize(envelope, EventEnvelope.Json),
        CreatedAt = envelope.OccurredAt
    });

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Payload).HasColumnType("jsonb");
            entity.HasIndex(x => x.CreatedAt).HasFilter("\"DispatchedAt\" IS NULL");
        });
        modelBuilder.Entity<InboxMessage>(entity =>
        {
            entity.ToTable("inbox");
            entity.HasKey(x => new { x.Consumer, x.MessageId });
            entity.Property(x => x.Consumer).HasMaxLength(64);
        });
    }
}

public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string Payload { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
}

public sealed class InboxMessage
{
    public string Consumer { get; set; } = "";
    public Guid MessageId { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}
