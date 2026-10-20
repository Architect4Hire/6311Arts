using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace Arts6311.Tests.EndToEnd;

/// <summary>
/// Turns the AppHost's model into one that leaves nothing behind: no data volumes, no persistent containers, and no
/// run-mode tools. Applied by <see cref="PlatformFixture"/> before the app is built, and checked on its own by
/// <c>AppHostModelTests</c>, which need no container runtime.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="ContainerLifetime.Persistent"/> container is reused by name across runs, and the test runs the same
/// AppHost as the developer's <c>aspire run</c>, so without this the test would attach to the developer's containers
/// and their data. Stripping the lifetime makes each container session-scoped; stripping the volumes makes each start
/// empty. The result is what makes "starts from empty volumes" true.
/// </para>
/// <para>
/// Only volumes are removed. The model declares no bind mounts today; <c>AppHostModelTests</c> fails the day one
/// appears, so whoever adds it decides whether it carries state.
/// </para>
/// </remarks>
internal static class EphemeralAppModel
{
    /// <summary>Run-mode tools the end-to-end flow does not use: one image fewer to pull in CI.</summary>
    public static readonly string[] RemovedResources = ["dbgate"];

    public static void Apply(IDistributedApplicationTestingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        foreach (var tool in builder.Resources.Where(resource => RemovedResources.Contains(resource.Name)).ToList())
        {
            builder.Resources.Remove(tool);
        }

        foreach (var container in builder.Resources.OfType<ContainerResource>())
        {
            foreach (var annotation in container.Annotations
                         .Where(annotation => annotation is ContainerLifetimeAnnotation
                             || annotation is ContainerMountAnnotation { Type: ContainerMountType.Volume })
                         .ToList())
            {
                container.Annotations.Remove(annotation);
            }
        }
    }
}
