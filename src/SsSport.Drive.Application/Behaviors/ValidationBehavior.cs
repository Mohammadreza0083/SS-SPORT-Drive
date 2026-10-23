using FluentValidation;
using MediatR;
using SsSport.Drive.Application.Exceptions;

namespace SsSport.Drive.Application.Behaviors;

/// <summary>
/// Runs every FluentValidation validator of the request; any failure stops it with a <see cref="RequestValidationException"/>.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <param name="validators">The request's validators (possibly none).</param>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>Validates the request, then runs the rest of the pipeline.</summary>
    /// <param name="request">The request.</param>
    /// <param name="next">The rest of the pipeline.</param>
    /// <param name="cancellationToken">Cancels validation and the request.</param>
    /// <returns>The handler's response.</returns>
    /// <exception cref="RequestValidationException">At least one rule failed.</exception>
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var context = new ValidationContext<TRequest>(request);
        var failures = new List<FluentValidation.Results.ValidationFailure>();
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(context, cancellationToken);
            failures.AddRange(result.Errors);
        }

        if (failures.Count > 0)
        {
            throw new RequestValidationException(failures
                .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).Distinct().ToArray(), StringComparer.Ordinal));
        }

        return await next(cancellationToken);
    }
}
