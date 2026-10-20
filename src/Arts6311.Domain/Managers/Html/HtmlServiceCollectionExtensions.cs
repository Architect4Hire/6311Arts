using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Domain.Managers.Html;

public static class HtmlServiceCollectionExtensions
{
    /// <summary>The shared allow-list sanitizer. Stateless between calls, so one instance serves every request.</summary>
    public static IServiceCollection AddArtsHtml(this IServiceCollection services)
    {
        services.TryAddSingleton<IHtmlSanitizer, AllowListHtmlSanitizer>();
        return services;
    }
}
