using SsSport.Drive.Domain.Players;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Domain.Links;

/// <summary>
/// The club, scout or agent a personal link is made for (L-1): a name, an optional organisation and country, and one to
/// five email addresses. Those emails pass the email gate on a new device (L-2) without counting as a forward.
/// </summary>
public sealed class Recipient : AggregateRoot<Guid>
{
    /// <summary>The longest name.</summary>
    public const int MaxNameLength = 120;

    /// <summary>The longest organisation.</summary>
    public const int MaxOrganisationLength = 120;

    /// <summary>The most emails a recipient may have.</summary>
    public const int MaxEmails = 5;

    /// <summary>The recipient's emails.</summary>
    private readonly List<EmailAddress> _emails = [];

    /// <summary>Initializes a recipient; use <see cref="Create"/>.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="name">The name.</param>
    /// <param name="createdAt">When it was created (UTC).</param>
    private Recipient(Guid id, string name, DateTimeOffset createdAt)
        : base(id)
    {
        Name = name;
        CreatedAt = createdAt;
    }

    /// <summary>Gets the person's name.</summary>
    public string Name { get; private set; }

    /// <summary>Gets the club or agency, or <see langword="null"/>.</summary>
    public string? Organisation { get; private set; }

    /// <summary>Gets the organisation's country, or <see langword="null"/>.</summary>
    public Nationality? Country { get; private set; }

    /// <summary>Gets the email addresses, in the order given.</summary>
    public IReadOnlyList<EmailAddress> Emails => _emails.AsReadOnly();

    /// <summary>Gets when the recipient was created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Creates a recipient.</summary>
    /// <param name="name">The person's name (trimmed, 1 to 120 characters).</param>
    /// <param name="organisation">The club or agency (at most 120 characters), or blank.</param>
    /// <param name="country">The organisation's country, or <see langword="null"/>.</param>
    /// <param name="emails">One to five emails; repeats are dropped.</param>
    /// <param name="createdAt">The current time (UTC); also seeds the time-ordered id.</param>
    /// <returns>The recipient.</returns>
    /// <exception cref="DomainException">
    /// <c>recipient.name_required</c>, <c>recipient.name_too_long</c>, <c>recipient.organisation_too_long</c>,
    /// <c>recipient.email_required</c> or <c>recipient.too_many_emails</c>.
    /// </exception>
    public static Recipient Create(string? name, string? organisation, Nationality? country, IEnumerable<EmailAddress> emails, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(emails);
        var trimmedName = name?.Trim();
        if (string.IsNullOrEmpty(trimmedName))
        {
            throw new DomainException("recipient.name_required", "Enter the recipient's name.");
        }

        if (trimmedName.Length > MaxNameLength)
        {
            throw new DomainException("recipient.name_too_long", $"Use a name of at most {MaxNameLength} characters.");
        }

        var trimmedOrganisation = string.IsNullOrWhiteSpace(organisation) ? null : organisation.Trim();
        if (trimmedOrganisation?.Length > MaxOrganisationLength)
        {
            throw new DomainException("recipient.organisation_too_long", $"Use an organisation of at most {MaxOrganisationLength} characters.");
        }

        var unique = emails.Distinct().ToList();
        if (unique.Count == 0)
        {
            throw new DomainException("recipient.email_required", "Enter at least one email address.");
        }

        if (unique.Count > MaxEmails)
        {
            throw new DomainException("recipient.too_many_emails", $"Enter at most {MaxEmails} email addresses.");
        }

        var recipient = new Recipient(Guid.CreateVersion7(createdAt), trimmedName, createdAt)
        {
            Organisation = trimmedOrganisation,
            Country = country,
        };
        recipient._emails.AddRange(unique);
        return recipient;
    }

    /// <summary>Checks whether an email is one of the recipient's own.</summary>
    /// <param name="email">The email (already lower-cased by <see cref="EmailAddress"/>).</param>
    /// <returns><see langword="true"/> when the recipient has it.</returns>
    public bool HasEmail(EmailAddress email) => _emails.Contains(email);
}
