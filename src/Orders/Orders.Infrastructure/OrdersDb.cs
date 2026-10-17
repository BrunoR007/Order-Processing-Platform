using Microsoft.EntityFrameworkCore;
using OrderProcessing.Messaging;
using OrderProcessing.Orders.Domain;

namespace OrderProcessing.Orders.Infrastructure;

public sealed class OrdersDb(DbContextOptions<OrdersDb> options) : MessageDbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("orders");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.RequestKey).IsUnique();
            entity.Property(x => x.RequestKey).HasMaxLength(128);
            entity.Property(x => x.LinesJson).HasColumnType("jsonb");
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Currency).HasMaxLength(3);
            entity.Property(x => x.Status).HasConversion<string>();
        });
    }
}
