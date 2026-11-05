using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Infrastructure.Identity;
using RockyTelecom.Infrastructure.Integrations;
using RockyTelecom.Infrastructure.Options;
using RockyTelecom.Infrastructure.Persistence;
using RockyTelecom.Infrastructure.Services;
using RockyTelecom.Infrastructure.Storage;

namespace RockyTelecom.Infrastructure;

/// <summary>
/// Wires persistence, Identity, caching, storage and the v1 mock integrations
/// (ARCHITECTURE §3 and §9). The Api project only calls <see cref="AddInfrastructure"/>.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=RockyTelecom;Trusted_Connection=True;TrustServerCertificate=True";

        // Connection-level retries are intentionally off (D-22): the MediatR transaction behaviour
        // opens a user-initiated transaction, which SqlServerRetryingExecutionStrategy refuses
        // unless every operation inside it is replayed through the strategy. Correctness of the
        // write path wins over automatic retries; a dropped connection surfaces as a normal error.
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        AddBackgroundJobs(services, connectionString);
        AddIdentity(services);
        AddCaching(services, configuration);
        AddOptions(services, configuration);
        AddServices(services);

        return services;
    }

    /// <summary>
    /// Hangfire (D-12) uses the same SQL Server as the application, so no extra infrastructure is
    /// required for the reservation-expiry and flash-sale jobs.
    /// </summary>
    private static void AddBackgroundJobs(IServiceCollection services, string connectionString)
    {
        services.AddHangfire(configuration => configuration
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
            {
                CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                QueuePollInterval = TimeSpan.Zero,
                UseRecommendedIsolationLevel = true,
                DisableGlobalLocks = true
            }));

        services.AddHangfireServer();
    }

    private static void AddIdentity(IServiceCollection services)
    {
        services.AddIdentityCore<AppUser>(options =>
        {
            options.Password.RequiredLength = 8;
            options.Password.RequireDigit = true;
            options.Password.RequireUppercase = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireNonAlphanumeric = false;

            // Phone is the login identifier, so email is optional (FR-06).
            options.User.RequireUniqueEmail = false;
            options.SignIn.RequireConfirmedAccount = false;
            options.SignIn.RequireConfirmedPhoneNumber = false;

            // Lockout after 5 failed attempts (D-10).
            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        })
        .AddRoles<IdentityRole>()
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders();

        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IUserAccountService, UserAccountService>();
    }

    private static void AddCaching(IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Cache:Provider"]?.Trim().ToLowerInvariant() ?? "memory";

        if (provider == "redis")
        {
            var redisConnection = configuration.GetConnectionString("Redis") ?? "localhost:6379";
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnection;
                options.InstanceName = "rocky:";
            });
            services.AddSingleton<ICache, RedisCacheService>();
            return;
        }

        // Local development uses the in-process cache (D-20).
        services.AddMemoryCache();
        services.AddSingleton<ICache, MemoryCacheService>();
    }

    private static void AddOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.Configure<CourierOptions>(configuration.GetSection(CourierOptions.SectionName));
        services.Configure<PaymentOptions>(configuration.GetSection(PaymentOptions.SectionName));
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
    }

    private static void AddServices(IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        services.AddSingleton<IImageStore, LocalImageStore>();

        // v1 integrations are mocks behind the production interfaces (D-06). Every gateway below
        // is a sandbox implementation; swapping in a real client is a registration change.
        services.AddScoped<IPaymentGateway, MockSslCommerzGateway>();
        services.AddScoped<IPaymentGateway, MockBkashGateway>();
        services.AddScoped<IPaymentGateway, MockNagadGateway>();
        services.AddScoped<IPaymentGatewayResolver, PaymentGatewayResolver>();
        services.AddSingleton<IPaymentUrlBuilder, PaymentUrlBuilder>();

        services.AddScoped<ICourierService, MockCourierService>();
        services.AddScoped<ISmsSender, LoggingSmsSender>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
    }
}
