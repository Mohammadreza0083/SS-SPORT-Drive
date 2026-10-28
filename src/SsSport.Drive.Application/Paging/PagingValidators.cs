using FluentValidation;

namespace SsSport.Drive.Application.Paging;

/// <summary>
/// Page numbers start at 1 and sizes are 1 to <see cref="PageRequest.MaxPageSize"/>. Errors are keyed by the query
/// parameter names (<c>page</c>, <c>pageSize</c>) so the client sees the field it sent.
/// </summary>
public sealed class PageRequestValidator : AbstractValidator<PageRequest>
{
    /// <summary>Builds the rules.</summary>
    public PageRequestValidator()
    {
        RuleFor(request => request.Page).GreaterThanOrEqualTo(1).OverridePropertyName("page");
        RuleFor(request => request.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).OverridePropertyName("pageSize");
    }
}

/// <summary>Sizes are 1 to <see cref="PageRequest.MaxPageSize"/>, and a cursor must be one this API issued.</summary>
public sealed class CursorRequestValidator : AbstractValidator<CursorRequest>
{
    /// <summary>Builds the rules.</summary>
    public CursorRequestValidator()
    {
        RuleFor(request => request.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).OverridePropertyName("pageSize");
        RuleFor(request => request.Cursor)
            .Must(CursorToken.IsWellFormed)
            .When(request => request.Cursor is not null)
            .WithMessage("The cursor is not valid. Use the nextCursor of the previous page, or leave it out for the first page.")
            .OverridePropertyName("cursor");
    }
}

/// <summary>
/// Validates the paging of every <see cref="IPagedRequest"/>, registered once for all of them, so a paged query needs
/// no paging rules of its own. Its errors keep the parameter names (<c>page</c>, <c>pageSize</c>).
/// </summary>
/// <typeparam name="TRequest">The paged request type.</typeparam>
public sealed class PagedRequestValidator<TRequest> : AbstractValidator<TRequest>
    where TRequest : IPagedRequest
{
    /// <summary>The shared rules for a page.</summary>
    private static readonly PageRequestValidator _paging = new();

    /// <summary>Builds the rules.</summary>
    public PagedRequestValidator() =>
        RuleFor(request => request.Paging).Custom((paging, context) =>
        {
            if (paging is null)
            {
                context.AddFailure("paging", "Paging is required.");
                return;
            }

            foreach (var failure in _paging.Validate(paging).Errors)
            {
                context.AddFailure(failure);
            }
        });
}

/// <summary>
/// Validates the paging of every <see cref="ICursorPagedRequest"/>, registered once for all of them. Its errors keep the
/// parameter names (<c>cursor</c>, <c>pageSize</c>).
/// </summary>
/// <typeparam name="TRequest">The cursor-paged request type.</typeparam>
public sealed class CursorPagedRequestValidator<TRequest> : AbstractValidator<TRequest>
    where TRequest : ICursorPagedRequest
{
    /// <summary>The shared rules for a cursor page.</summary>
    private static readonly CursorRequestValidator _paging = new();

    /// <summary>Builds the rules.</summary>
    public CursorPagedRequestValidator() =>
        RuleFor(request => request.Paging).Custom((paging, context) =>
        {
            if (paging is null)
            {
                context.AddFailure("paging", "Paging is required.");
                return;
            }

            foreach (var failure in _paging.Validate(paging).Errors)
            {
                context.AddFailure(failure);
            }
        });
}
