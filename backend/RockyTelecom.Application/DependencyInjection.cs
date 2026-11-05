using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using RockyTelecom.Application.Common.Behaviours;

namespace RockyTelecom.Application;

/// <summary>
/// Registers use cases: MediatR handlers, FluentValidation validators and the pipeline order
/// validation → logging → transaction (ARCHITECTURE §3).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(assembly);
            configuration.AddOpenBehavior(typeof(ValidationBehaviour<,>));
            configuration.AddOpenBehavior(typeof(LoggingBehaviour<,>));
            configuration.AddOpenBehavior(typeof(TransactionBehaviour<,>));
        });

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        return services;
    }
}
