using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Domain.Primitives;
using SsSport.Drive.Infrastructure.Persistence;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Persistence;

/// <summary>Raised when a <see cref="Note"/> is renamed.</summary>
/// <param name="NoteId">The renamed note.</param>
internal sealed record NoteRenamed(Guid NoteId) : IDomainEvent;

/// <summary>A minimal aggregate used to exercise the generic repository and the unit of work.</summary>
internal sealed class Note : AggregateRoot<Guid>
{
    /// <summary>Creates a note.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="title">The title.</param>
    public Note(Guid id, string title)
        : base(id) => Title = title;

    /// <summary>Gets the title.</summary>
    public string Title { get; private set; }

    /// <summary>Changes the title and raises <see cref="NoteRenamed"/>.</summary>
    /// <param name="title">The new title.</param>
    public void Rename(string title)
    {
        Title = title;
        RaiseDomainEvent(new NoteRenamed(Id));
    }
}

/// <summary>A test-only EF Core context that maps <see cref="Note"/> the way production contexts map aggregates.</summary>
/// <param name="options">The context options.</param>
internal sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    /// <summary>Maps the test aggregate.</summary>
    /// <param name="modelBuilder">The model builder.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Note>(note =>
        {
            note.HasKey(n => n.Id);
            note.Property(n => n.Title).HasMaxLength(200).IsRequired();
        });
        modelBuilder.IgnoreDomainEvents();
    }
}
