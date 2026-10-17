using Microsoft.EntityFrameworkCore;
using OrderProcessing.Messaging;

namespace OrderProcessing.Notifications.Service;

public sealed class NotificationsDb(DbContextOptions<NotificationsDb> options) : MessageDbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("notifications");
            entity.HasKey(x => x.OrderId);
        });
    }
}

public sealed class Notification
{
    public Guid OrderId { get; set; }
    public string Outcome { get; set; } = "";
    public DateTimeOffset RecordedAt { get; set; }
}
