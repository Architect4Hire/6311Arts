using System.Diagnostics;
using Arts6311.Domain.Managers;
using Arts6311.Domain.Managers.Correlation;
using Arts6311.Domain.Managers.Outbox;
using Microsoft.Extensions.DependencyInjection;

namespace Arts6311.Tests.Kernel;

public sealed class CorrelationContextTests
{
    [Fact]
    public void InsideAnActivityTheCorrelationIdIsTheTraceId()
    {
        using var activity = new Activity("test").SetIdFormat(ActivityIdFormat.W3C).Start();

        var current = new CorrelationContext().Current;

        // Same 128 bits, same hex: searching the dashboard for a row's CorrelationId finds its trace.
        Assert.Equal(activity.TraceId.ToHexString(), current.ToString("N"));
    }

    [Fact]
    public void ANestedActivityKeepsItsParentsCorrelationId()
    {
        using var parent = new Activity("request").SetIdFormat(ActivityIdFormat.W3C).Start();
        var context = new CorrelationContext();
        var atRequest = context.Current;

        using var child = new Activity("sql").SetIdFormat(ActivityIdFormat.W3C).Start();

        Assert.Equal(atRequest, context.Current);
    }

    [Fact]
    public void WithoutAnActivityTheIdIsFixedPerScopeAndNeverEmpty()
    {
        Activity.Current = null;
        using var services = new ServiceCollection().AddArtsCorrelation().BuildServiceProvider(validateScopes: true);
        using var first = services.CreateScope();
        using var second = services.CreateScope();

        var a = first.ServiceProvider.GetRequiredService<ICorrelationContext>();
        var again = first.ServiceProvider.GetRequiredService<ICorrelationContext>();
        var b = second.ServiceProvider.GetRequiredService<ICorrelationContext>();

        Assert.NotEqual(Guid.Empty, a.Current);
        Assert.Equal(a.Current, a.Current);
        Assert.Equal(a.Current, again.Current);
        Assert.NotEqual(a.Current, b.Current);
    }

    [Fact]
    public void TheKernelRegistersTheCorrelationContextAndTheOutbox()
    {
        var services = new ServiceCollection().AddArtsKernel();

        Assert.Contains(services, d => d.ServiceType == typeof(ICorrelationContext) && d.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, d => d.ServiceType == typeof(IOutboxWriter) && d.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, d => d.ServiceType == typeof(IOutboxDispatcher) && d.Lifetime == ServiceLifetime.Scoped);
    }
}
