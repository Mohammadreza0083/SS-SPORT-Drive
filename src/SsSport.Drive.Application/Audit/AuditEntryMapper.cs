using Riok.Mapperly.Abstractions;
using SsSport.Drive.Domain.Audit;

namespace SsSport.Drive.Application.Audit;

/// <summary>Maps <see cref="AuditEntry"/> to <see cref="AuditEntryDto"/> at compile time (Mapperly).</summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class AuditEntryMapper
{
    /// <summary>Maps an audit entry to its DTO.</summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The DTO.</returns>
    public static partial AuditEntryDto ToDto(this AuditEntry entry);
}
