using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Vyxum.Platform.Abstractions;
using Vyxum.Platform.Tests.Modules;
using Xunit;

namespace Vyxum.Platform.Tests.Identity;

public class IdentityTests
{
    private static PermissionDefinition Def(string id, string key = "samples.Permission") =>
        new() { Id = id, NameKey = key, DefaultRoles = [BuiltInRoles.Operator] };

    private static TestModule Module(string id, params PermissionDefinition[] permissions) =>
        new(new ModuleManifest { Id = id, Version = new Version(1, 0), Permissions = permissions });

    private static IReadOnlyList<string> Problems(params TestModule[] modules) =>
        ModuleValidator.FindProblems(modules.Select(m => new ModuleDescriptor(m)).ToList());

    [Fact]
    public void Valid_permissions_have_no_problems()
    {
        Problems(Module("samples.orders", Def("samples.orders.cancel"), Def("samples.orders.export"))).ShouldBeEmpty();
    }

    [Fact]
    public void Invalid_permission_and_role_ids_are_problems()
    {
        var problems = Problems(Module("samples.orders",
            new PermissionDefinition { Id = "Orders.Cancel", NameKey = "samples.X", DefaultRoles = ["Shift Supervisor"] }));

        problems.ShouldContain("Module 'samples.orders' declares an invalid permission id 'Orders.Cancel'.");
        problems.ShouldContain("Permission 'Orders.Cancel' of module 'samples.orders' names an invalid role id 'Shift Supervisor'.");
    }

    [Fact]
    public void A_permission_without_a_name_key_is_a_problem()
    {
        Problems(Module("samples.orders", Def("samples.orders.cancel", key: " ")))
            .ShouldHaveSingleItem().ShouldBe("Permission 'samples.orders.cancel' of module 'samples.orders' has no name key.");
    }

    [Fact]
    public void A_permission_declared_by_two_modules_is_a_problem()
    {
        Problems(Module("a", Def("shared.cancel")), Module("b", Def("shared.cancel")))
            .ShouldHaveSingleItem().ShouldBe("Permission 'shared.cancel' is declared more than once: 'a', 'b'.");
    }

    [Fact]
    public async Task Without_a_users_module_nobody_signs_in_and_everything_is_allowed()
    {
        var services = new ServiceCollection();
        services.AddVyxumPlatform(new ConfigurationBuilder().Build(), [Module("samples.orders", Def("samples.orders.cancel"))]);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<ICurrentActor>().Person.ShouldBeNull();
        scope.ServiceProvider.GetRequiredService<IAuthorization>().IsAllowed("samples.orders.cancel").ShouldBeTrue();
        scope.ServiceProvider.GetRequiredService<IAuthorization>().IsAllowed("anything.else").ShouldBeTrue();

        var approval = scope.ServiceProvider.GetRequiredService<ISupervisorApproval>();
        var ok = await approval.ApproveAsync(new ApprovalRequest("samples.orders.cancel", "  Last ingredient ", "Order 7", null), TestContext.Current.CancellationToken);
        ok.ShouldBe(new ApprovalResult(true, null, "Last ingredient", null));
        var refused = await approval.ApproveAsync(new ApprovalRequest("samples.orders.cancel", " ", "Order 7", null), TestContext.Current.CancellationToken);
        refused.Approved.ShouldBeFalse();
        refused.RejectionCode.ShouldBe(ApprovalErrors.ReasonRequired);
    }

    [Fact]
    public void The_catalog_lists_permissions_by_module()
    {
        var services = new ServiceCollection();
        services.AddVyxumPlatform(new ConfigurationBuilder().Build(),
            [Module("b.second", Def("b.second.do")), Module("a.first", Def("a.first.do"), Def("a.first.undo"))]);
        using var provider = services.BuildServiceProvider();

        var catalog = provider.GetRequiredService<IPermissionCatalog>();
        catalog.Permissions.Select(p => $"{p.ModuleId}:{p.Definition.Id}").ShouldBe(["a.first:a.first.do", "a.first:a.first.undo", "b.second:b.second.do"]);
        catalog.Find("a.first.undo")!.ModuleId.ShouldBe("a.first");
        catalog.Find("missing").ShouldBeNull();
    }

    [Fact]
    public async Task A_users_module_replaces_the_defaults_but_not_the_catalog()
    {
        var users = new TestModule(
            new ModuleManifest { Id = "identity", Version = new Version(1, 0), Provides = ["identity.users"] },
            (services, _) =>
            {
                services.AddScoped<ICurrentActor, FixedActor>();
                services.AddScoped<IAuthorization, DenyAll>();
                services.AddSingleton<IPermissionCatalog>(new EmptyCatalog());
            });
        var services = new ServiceCollection();
        services.AddVyxumPlatform(new ConfigurationBuilder().Build(), [users]);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<ICurrentActor>().Person.ShouldBe(new Actor("u1", "Ayşe Demir"));
        scope.ServiceProvider.GetRequiredService<IAuthorization>().IsAllowed("x").ShouldBeFalse();
        scope.ServiceProvider.GetRequiredService<IPermissionCatalog>().ShouldBeOfType<PermissionCatalog>();
    }

    private sealed class FixedActor : ICurrentActor
    {
        public Actor? Person { get; } = new("u1", "Ayşe Demir");
    }

    private sealed class DenyAll : IAuthorization
    {
        public bool IsAllowed(Permission permission) => false;
    }

    private sealed class EmptyCatalog : IPermissionCatalog
    {
        public IReadOnlyList<DeclaredPermission> Permissions => [];

        public DeclaredPermission? Find(Permission permission) => null;
    }
}
