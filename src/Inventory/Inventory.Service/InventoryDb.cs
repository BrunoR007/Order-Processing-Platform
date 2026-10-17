using Microsoft.EntityFrameworkCore;
using OrderProcessing.Messaging;

namespace OrderProcessing.Inventory.Service;

public sealed class InventoryDb(DbContextOptions<InventoryDb> options) : MessageDbContext(options)
{
    public DbSet<StockItem> Stock => Set<StockItem>();
    public DbSet<Reservation> Reservations => Set<Reservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<StockItem>(entity =>
        {
            entity.ToTable("stock", table => table.HasCheckConstraint("stock_nonnegative", "\"Available\" >= 0"));
            entity.HasKey(x => x.Sku);
            entity.HasData(new StockItem { Sku = "BOOK-DOTNET", Available = 50 },
                new StockItem { Sku = "MUG-EVENTS", Available = 50 },
                new StockItem { Sku = "SHIRT-ASYNC", Available = 50 });
        });
        modelBuilder.Entity<Reservation>(entity =>
        {
            entity.ToTable("reservations");
            entity.HasKey(x => x.OrderId);
            entity.Property(x => x.LinesJson).HasColumnType("jsonb");
        });
    }
}

public sealed class StockItem
{
    public string Sku { get; set; } = "";
    public int Available { get; set; }
}

public sealed class Reservation
{
    public Guid OrderId { get; set; }
    public string LinesJson { get; set; } = "[]";
    public string Status { get; set; } = "Reserved";
}
