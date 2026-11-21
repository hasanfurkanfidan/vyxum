using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Vyxum.Platform.Abstractions;
using Vyxum.Platform.Tests.Modules;
using Xunit;

namespace Vyxum.Platform.Tests.Data;

public class ModuleDbContextTests
{
    private static DbContextOptions<T> Options<T>()
        where T : DbContext =>
        new DbContextOptionsBuilder<T>().UseSqlite("Data Source=:memory:").Options;

    [Fact]
    public void Every_table_gets_the_module_prefix_once()
    {
        using var context = new ShopDbContext(Options<ShopDbContext>(), "shop");

        context.Model.FindEntityType(typeof(Item))!.GetTableName().ShouldBe("shop_Items");
        context.Model.FindEntityType(typeof(Tag))!.GetTableName().ShouldBe("shop_Tags");
        context.Model.FindEntityType(typeof(Price))!.GetTableName().ShouldBe("shop_Items");
    }

    [Theory]
    [InlineData("")]
    [InlineData("1shop")]
    [InlineData("shop_x")]
    public void Invalid_prefix_is_rejected(string prefix)
    {
        using var context = new ShopDbContext(Options<ShopDbContext>(), prefix);

        Should.Throw<InvalidOperationException>(() => context.Model).Message.ShouldContain("TablePrefix");
    }

    [Fact]
    public void A_module_can_register_only_one_DbContext()
    {
        var module = new TestModule(
            new ModuleManifest { Id = "shop", Version = new Version(1, 0) },
            (_, context) =>
            {
                context.AddDbContext<ShopDbContext>();
                context.AddDbContext<OtherDbContext>();
            });

        Should.Throw<InvalidOperationException>(() =>
                new ServiceCollection().AddVyxumPlatform(new ConfigurationBuilder().Build(), [module]))
            .Message.ShouldContain("a module owns exactly one DbContext");
    }

    [Fact]
    public void Two_modules_cannot_share_a_DbContext()
    {
        static TestModule Module(string id) =>
            new(new ModuleManifest { Id = id, Version = new Version(1, 0) }, (_, c) => c.AddDbContext<OtherDbContext>());

        Should.Throw<ModuleValidationException>(() =>
                new ServiceCollection().AddVyxumPlatform(new ConfigurationBuilder().Build(), [Module("a"), Module("b")]))
            .Problems.ShouldHaveSingleItem().ShouldContain("is registered by more than one module: 'a', 'b'");
    }

    private sealed class ShopDbContext(DbContextOptions<ShopDbContext> options, string prefix) : ModuleDbContext(options)
    {
        public override string TablePrefix => prefix;

        protected override void ConfigureModel(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Item>(e =>
            {
                e.ToTable("Items");
                e.OwnsOne(i => i.Price);
            });
            modelBuilder.Entity<Tag>().ToTable(prefix + "_Tags");
        }

        // Each prefix needs its own cached model.
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);
            optionsBuilder.ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, PrefixCacheKeyFactory>();
        }
    }

    private sealed class PrefixCacheKeyFactory : Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) =>
            (context.GetType(), ((ModuleDbContext)context).TablePrefix, designTime);
    }

    private sealed class OtherDbContext(DbContextOptions<OtherDbContext> options) : ModuleDbContext(options)
    {
        public override string TablePrefix => "other";

        protected override void ConfigureModel(ModelBuilder modelBuilder)
        {
        }
    }

    private sealed class Item
    {
        public int Id { get; set; }

        public Price Price { get; set; } = new();
    }

    private sealed class Price
    {
        public int Amount { get; set; }
    }

    private sealed class Tag
    {
        public int Id { get; set; }
    }
}
