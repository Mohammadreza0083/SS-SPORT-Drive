using FluentValidation;
using SsSport.Drive.Application.Paging;

namespace SsSport.Drive.Application.Validation;

/// <summary>
/// Shared FluentValidation rules for the inputs many requests carry (decisions §1b rule 3): ids, Telegram ids, paging,
/// and size limits on link codes, opaque tokens, slugs and URLs. Each request's validator composes these, so a
/// malformed value is refused with 400 before a handler or the database sees it.
/// <para>
/// Link codes, tokens and slugs are only <em>bounded</em> here. Their format is checked by the handlers, which answer
/// a malformed value exactly like an unknown one (404 / 401), so codes and tokens cannot be probed (decisions §7).
/// </para>
/// </summary>
public static class InputRules
{
    /// <summary>The longest link code accepted before the handler looks at it (real codes are 6 characters).</summary>
    public const int MaxCodeLength = 64;

    /// <summary>The longest opaque token accepted (session, device, sign-in); issued tokens are 43 characters.</summary>
    public const int MaxTokenLength = 512;

    /// <summary>The longest slug accepted.</summary>
    public const int MaxSlugLength = 200;

    /// <summary>The longest URL accepted.</summary>
    public const int MaxUrlLength = 2048;

    /// <summary>Requires a link code of bounded length; its format is checked by the handler (uniform 404).</summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <param name="rule">The rule.</param>
    /// <returns>The rule options, for chaining.</returns>
    public static IRuleBuilderOptions<T, string> BoundedCode<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MaximumLength(MaxCodeLength).WithMessage($"A link code is at most {MaxCodeLength} characters.");

    /// <summary>Allows no token, or one of bounded length; its format is checked by the handler (uniform 401).</summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <param name="rule">The rule.</param>
    /// <returns>The rule options, for chaining.</returns>
    public static IRuleBuilderOptions<T, string?> BoundedToken<T>(this IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(MaxTokenLength).WithMessage($"A token is at most {MaxTokenLength} characters.");

    /// <summary>Requires a slug of bounded length; its format is checked by the handler (uniform 404).</summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <param name="rule">The rule.</param>
    /// <returns>The rule options, for chaining.</returns>
    public static IRuleBuilderOptions<T, string> BoundedSlug<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MaximumLength(MaxSlugLength).WithMessage($"A slug is at most {MaxSlugLength} characters.");

    /// <summary>
    /// Allows no URL, or one of bounded length. The domain value objects (<c>DriveFolderLink</c>, <c>VolleyboxProfile</c>)
    /// check the format, accept links pasted without a scheme, and answer 422 with a helpful message.
    /// </summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <param name="rule">The rule.</param>
    /// <returns>The rule options, for chaining.</returns>
    public static IRuleBuilderOptions<T, string?> BoundedUrl<T>(this IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(MaxUrlLength).WithMessage($"Use a link of at most {MaxUrlLength} characters.");

    /// <summary>Requires a non-empty id.</summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <param name="rule">The rule.</param>
    /// <returns>The rule options, for chaining.</returns>
    public static IRuleBuilderOptions<T, Guid> ValidId<T>(this IRuleBuilder<T, Guid> rule) =>
        rule.NotEqual(Guid.Empty).WithMessage("The id is required.");

    /// <summary>Requires a Telegram user id (a positive number).</summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <param name="rule">The rule.</param>
    /// <returns>The rule options, for chaining.</returns>
    public static IRuleBuilderOptions<T, long> ValidTelegramId<T>(this IRuleBuilder<T, long> rule) =>
        rule.GreaterThan(0).WithMessage("The Telegram id is required.");

    /// <summary>Requires a valid page (number from 1, size 1 to the maximum).</summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <param name="rule">The rule.</param>
    /// <returns>The rule options, for chaining.</returns>
    public static IRuleBuilderOptions<T, PageRequest> ValidPaging<T>(this IRuleBuilder<T, PageRequest> rule) =>
        rule.NotNull().SetValidator(new PageRequestValidator());

    /// <summary>Requires a valid cursor page.</summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <param name="rule">The rule.</param>
    /// <returns>The rule options, for chaining.</returns>
    public static IRuleBuilderOptions<T, CursorRequest> ValidCursorPaging<T>(this IRuleBuilder<T, CursorRequest> rule) =>
        rule.NotNull().SetValidator(new CursorRequestValidator());
}
