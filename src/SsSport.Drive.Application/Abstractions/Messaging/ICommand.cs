using MediatR;

namespace SsSport.Drive.Application.Abstractions.Messaging;

/// <summary>Marker for every command; the transaction behaviour commits commands and never queries.</summary>
public interface IBaseCommand;

/// <summary>A request that changes state and returns nothing.</summary>
public interface ICommand : IRequest, IBaseCommand;

/// <summary>A request that changes state and returns a result (for example the new id).</summary>
/// <typeparam name="TResponse">The result type.</typeparam>
public interface ICommand<out TResponse> : IRequest<TResponse>, IBaseCommand;

/// <summary>Handles a command that returns nothing.</summary>
/// <typeparam name="TCommand">The command type.</typeparam>
public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand>
    where TCommand : ICommand;

/// <summary>Handles a command that returns a result.</summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <typeparam name="TResponse">The result type.</typeparam>
public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>;
