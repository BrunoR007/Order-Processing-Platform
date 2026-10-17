using Microsoft.EntityFrameworkCore;
using OrderProcessing.Messaging;

namespace OrderProcessing.Payments.Service;

public sealed class PaymentsDb(DbContextOptions<PaymentsDb> options) : MessageDbContext(options)
{
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Payment>(entity =>
        {
            entity.ToTable("payments");
            entity.HasKey(x => x.OrderId);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
        });
    }
}

public sealed class Payment
{
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public bool Approved { get; set; }
}
