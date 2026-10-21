using Arts6311.Domain.Managers.Email;
using Arts6311.Domain.Modules.Forms.Business;
using Arts6311.Domain.Modules.Forms.Data;
using Arts6311.Domain.Modules.Forms.Data.Repositories;
using Arts6311.Domain.Modules.Forms.Facade;
using Arts6311.Domain.Modules.Forms.Managers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Domain.Modules.Forms;

public static class FormsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Forms module: the seam for the API (the public forms and the CMS inbox), and the four email
    /// composers for the Worker, keyed by the kind they compose. Requires <c>AddArtsKernel</c>, the
    /// <c>ArtsDbContext</c> and <c>AddMediaModule</c>, whose facade the submissions' pictures go through.
    /// </summary>
    /// <param name="configuration">The host's configuration; the Worker must supply <c>Forms:StaffAddress</c>.</param>
    public static IServiceCollection AddFormsModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddScoped<IFormsFacade, FormsFacade>();
        services.TryAddScoped<IFormsBusiness, FormsBusiness>();
        services.TryAddScoped<IFormsDataLayer, FormsDataLayer>();
        services.TryAddScoped<IFormsRepository, FormsRepository>();

        // Validated where it is used: only the composers read it, so only a host that sends email fails without it.
        services.AddOptions<FormsOptions>().Bind(configuration.GetSection(FormsOptions.SectionName));

        services.TryAddKeyedScoped<IEmailComposer, SubmissionReceivedComposer>(FormsEmailKinds.SubmissionReceived);
        services.TryAddKeyedScoped<IEmailComposer, SubmissionStaffComposer>(FormsEmailKinds.SubmissionStaff);
        services.TryAddKeyedScoped<IEmailComposer, ContactReceivedComposer>(FormsEmailKinds.ContactReceived);
        services.TryAddKeyedScoped<IEmailComposer, ContactStaffComposer>(FormsEmailKinds.ContactStaff);
        return services;
    }
}
