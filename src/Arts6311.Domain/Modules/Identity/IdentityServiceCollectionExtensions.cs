using Arts6311.Domain.Managers.Email;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Modules.Identity.Business;
using Arts6311.Domain.Modules.Identity.Data;
using Arts6311.Domain.Modules.Identity.Data.Entities;
using Arts6311.Domain.Modules.Identity.Data.Repositories;
using Arts6311.Domain.Modules.Identity.Facade;
using Arts6311.Domain.Modules.Identity.Managers;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Domain.Modules.Identity;

public static class IdentityServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Identity module: the stores on <c>ArtsDbContext</c>, the password policy, the seam and the
    /// email composers. Called by the API (internal routes), the Worker (composers) and the MigrationService
    /// (the seeder). Requires <c>AddArtsKernel</c> and the <c>ArtsDbContext</c> registration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>AddIdentityCore</c>, not <c>AddIdentity</c>: the latter adds cookie authentication schemes, and the API
    /// takes no cookies at all — the gateway owns the browser session (ADR 0002). There is no
    /// <c>SignInManager</c> either, for the same reason, which is why Business drives lockout itself.
    /// </para>
    /// <para>
    /// It returns the <see cref="IdentityBuilder"/> so the host can add the token provider that mints
    /// confirmation and reset tokens: <c>DataProtectorTokenProvider</c> lives in the ASP.NET Core framework
    /// assembly, and pulling that in here would put <c>HttpContext</c> and all of MVC within reach of the Domain.
    /// Hosts that need tokens call <c>AddArtsIdentityTokenProvider()</c> (ADR 0023).
    /// </para>
    /// </remarks>
    public static IdentityBuilder AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var identity = services.AddIdentityCore<ArtsUser>(options =>
            {
                options.User.RequireUniqueEmail = true;

                // Length plus the bundled list, not character classes: composition rules push people towards
                // predictable substitutions without adding real entropy (ADR 0023).
                options.Password.RequiredLength = IdentityPolicy.PasswordMinLength;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;

                options.Lockout.MaxFailedAccessAttempts = IdentityPolicy.MaxFailedAccessAttempts;
                options.Lockout.DefaultLockoutTimeSpan = IdentityPolicy.LockoutDuration;
                options.Lockout.AllowedForNewUsers = true;

                // Business decides what an unconfirmed address means, because only staff are refused for it
                // (auth.md); Identity's own flag would refuse members too.
                options.SignIn.RequireConfirmedEmail = false;
            })
            .AddRoles<ArtsRole>()
            .AddEntityFrameworkStores<ArtsDbContext>()

            // Applies to members as well as staff: the staff length rule already rejects all but one entry on
            // the list, so members are where it does the work (ADR 0023).
            .AddPasswordValidator<BreachedPasswordValidator>();

        services.TryAddScoped<IIdentityFacade, IdentityFacade>();
        services.TryAddScoped<IIdentityBusiness, IdentityBusiness>();
        services.TryAddScoped<IIdentityDataLayer, IdentityDataLayer>();
        services.TryAddScoped<IIdentityRepository, IdentityRepository>();

        // Keyed by the kind they compose, so the kernel's one email handler resolves them without the kernel
        // ever naming this module (backend.md).
        services.TryAddKeyedScoped<IEmailComposer, EmailConfirmationComposer>(IdentityEmailKinds.EmailConfirmation);
        services.TryAddKeyedScoped<IEmailComposer, PasswordResetComposer>(IdentityEmailKinds.PasswordReset);

        services.AddOptions<SeedAdminOptions>().Bind(configuration.GetSection(SeedAdminOptions.SectionName));

        return identity;
    }

    /// <summary>
    /// Adds the first-Admin seeder. The MigrationService only: it is the one host that runs seeders, and the
    /// seed password is passed to nothing else.
    /// </summary>
    public static IServiceCollection AddIdentitySeeder(this IServiceCollection services)
    {
        services.AddScoped<IDataSeeder, IdentityUserSeeder>();
        return services;
    }
}
