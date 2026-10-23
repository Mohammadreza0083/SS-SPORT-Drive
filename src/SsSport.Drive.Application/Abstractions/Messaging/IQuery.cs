using MediatR;

namespace SsSport.Drive.Application.Abstractions.Messaging;

/// <summary>A read-only request. Queries never write and are never committed.</summary>
/// <typeparam name="TResponse">The result type, usually a response DTO.</typeparam>
public interface IQuery<out TResponse> : IRequest<TResponse>;

/// <summary>Handles a query.</summary>
/// <typeparam name="TQuery">The query type.</typeparam>
/// <typeparam name="TResponse">The result type.</typeparam>
public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>;
