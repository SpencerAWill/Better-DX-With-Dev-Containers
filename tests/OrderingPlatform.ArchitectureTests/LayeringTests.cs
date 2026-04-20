using System.Reflection;

namespace OrderingPlatform.ArchitectureTests;

/// <summary>
/// Cross-assembly layering rules. These tests run against the compiled assemblies, so they only
/// cover code that has actually been built into a project — new apps/libs must be added to the
/// <see cref="LoadAllProductionAssemblies"/> list to be covered.
/// </summary>
public class LayeringTests
{
    [Fact]
    public void Apps_should_not_reference_other_apps_directly()
    {
        // Apps talk over HTTP/messaging, never by assembly reference. This test fails the moment
        // someone adds a ProjectReference from one `apps/` project to another.
        var appAssemblies = new[]
        {
            typeof(OrderingPlatform.Ordering.Api.Endpoints.CheckoutEndpoints).Assembly,
            typeof(OrderingPlatform.Notification.Functions.HealthCheck).Assembly,
        };

        foreach (var appAssembly in appAssemblies)
        {
            var otherAppNames = appAssemblies
                .Where(a => a != appAssembly)
                .Select(a => a.GetName().Name!)
                .ToArray();

            var result = Types.InAssembly(appAssembly)
                .Should()
                .NotHaveDependencyOnAny(otherAppNames)
                .GetResult();

            result.IsSuccessful.ShouldBeTrue(
                $"{appAssembly.GetName().Name} has a forbidden dependency on another app: " +
                string.Join(", ", result.FailingTypeNames ?? []));
        }
    }

    [Fact]
    public void Data_library_should_not_reference_any_app()
    {
        // Data libs are leaves — apps reference them, not the other way around.
        var dataAssembly = typeof(OrderingPlatform.Ordering.Data.AppDbContext).Assembly;
        var appNames = new[] { "OrderingPlatform.Ordering.Api", "OrderingPlatform.Notification.Functions" };

        var result = Types.InAssembly(dataAssembly)
            .Should()
            .NotHaveDependencyOnAny(appNames)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "OrderingPlatform.Ordering.Data has a forbidden dependency on an app assembly.");
    }

    [Fact]
    public static void Production_assemblies_should_not_reference_test_frameworks()
    {
        var productionAssemblies = LoadAllProductionAssemblies();
        var forbidden = new[] { "xunit", "xunit.core", "Moq", "NSubstitute", "Shouldly", "NetArchTest.Rules" };

        foreach (var assembly in productionAssemblies)
        {
            var refs = assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
            foreach (var bad in forbidden)
            {
                refs.ShouldNotContain(bad,
                    $"{assembly.GetName().Name} references the test-only package {bad}.");
            }
        }
    }

    private static IReadOnlyCollection<Assembly> LoadAllProductionAssemblies() =>
    [
        typeof(OrderingPlatform.Ordering.Api.Endpoints.CheckoutEndpoints).Assembly,
        typeof(OrderingPlatform.Notification.Functions.HealthCheck).Assembly,
        typeof(OrderingPlatform.Ordering.Data.AppDbContext).Assembly,
    ];
}
