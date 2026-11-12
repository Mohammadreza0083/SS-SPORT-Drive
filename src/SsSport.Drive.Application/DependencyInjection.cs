using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SsSport.Drive.Application.Abstractions.Authentication;
using SsSport.Drive.Application.Abstractions.Email;
using SsSport.Drive.Application.Abstractions.Events;
using SsSport.Drive.Application.Accounts;
using SsSport.Drive.Application.Behaviors;
using SsSport.Drive.Application.Emails;
using SsSport.Drive.Application.Events;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Application.Telegram.Notices;
using SsSport.Drive.Domain.Accounts;

namespace SsSport.Drive.Application;

/// <summary>Registers the Application layer's services with the dependency-injection container.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Adds MediatR with its pipeline (logging → authorization → validation → transaction → handler),
    /// the validators and domain event handlers of this assembly, the shared paging validators, the event dispatcher, the bot notice outbox (T-8), the system clock, and an anonymous
    /// caller for hosts without users (the API host replaces it with the signed-in user).
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(AssemblyReference.Assembly);
            configuration.AddOpenBehavior(typeof(LoggingBehavior<,>));
            configuration.AddOpenBehavior(typeof(AuthorizationBehavior<,>));
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
            configuration.AddOpenBehavior(typeof(TransactionBehavior<,>));
        });

        services.AddValidatorsFromAssembly(AssemblyReference.Assembly, includeInternalTypes: true);
        // Open generics: every paged query gets its paging checked without writing rules of its own.
        services.AddTransient(typeof(IValidator<>), typeof(PagedRequestValidator<>));
        services.AddTransient(typeof(IValidator<>), typeof(CursorPagedRequestValidator<>));
        services.Scan(AssemblyReference.Assembly);
        services.AddScoped<IEmailComposer, GateCodeComposer>();
        services.AddScoped<IEmailComposer, ForwardKeptComposer>();
        services.AddScoped<IEmailComposer>(provider => ActivatorUtilities.CreateInstance<SignInEmailComposer>(provider, SignInAudience.Admin));
        services.AddScoped<IEmailComposer>(provider => ActivatorUtilities.CreateInstance<SignInEmailComposer>(provider, SignInAudience.Viewer));
        services.TryAddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.TryAddScoped<IBackgroundEventRunner, BackgroundEventRunner>();
        services.TryAddScoped<IBotNoticeOutbox, BotNoticeOutbox>();
        services.TryAddScoped<ICurrentUser, AnonymousCurrentUser>();
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }

    /// <summary>
    /// Registers every concrete <see cref="IDomainEventHandler{TEvent}"/> and <see cref="IBackgroundEventHandler{TEvent}"/>
    /// in an assembly as scoped.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="assembly">The assembly to scan.</param>
    private static void Scan(this IServiceCollection services, System.Reflection.Assembly assembly)
    {
        var handlers = assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .SelectMany(type => type.GetInterfaces()
                .Where(contract => contract.IsGenericType && (contract.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>) || contract.GetGenericTypeDefinition() == typeof(IBackgroundEventHandler<>)))
                .Select(contract => (Contract: contract, Implementation: type)));

        foreach (var (contract, implementation) in handlers)
        {
            services.AddScoped(contract, implementation);
        }
    }
}
