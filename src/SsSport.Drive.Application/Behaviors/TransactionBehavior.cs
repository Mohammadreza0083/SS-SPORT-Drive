using MediatR;
using SsSport.Drive.Application.Abstractions.Messaging;
using SsSport.Drive.Application.Abstractions.Persistence;

namespace SsSport.Drive.Application.Behaviors;

/// <summary>
/// Innermost behaviour: commits a command's staged changes through the unit of work once its handler succeeds.
/// Handlers therefore never call <c>SaveChangesAsync</c> themselves, and queries never write.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <param name="unitOfWork">The request's unit of work.</param>
public sealed class TransactionBehavior<TRequest, TResponse>(IUnitOfWork unitOfWork) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>Runs the handler and, for commands, commits its changes.</summary>
    /// <param name="request">The request.</param>
    /// <param name="next">The handler.</param>
    /// <param name="cancellationToken">Cancels the request and the commit.</param>
    /// <returns>The handler's response.</returns>
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var response = await next(cancellationToken);

        if (request is IBaseCommand)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return response;
    }
}
