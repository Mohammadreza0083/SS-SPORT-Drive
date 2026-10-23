using Ardalis.Specification;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Persistence;

/// <summary>Notes whose title starts with a prefix, ordered by title.</summary>
internal sealed class NotesByTitlePrefixSpec : Specification<Note>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="prefix">The title prefix to match.</param>
    public NotesByTitlePrefixSpec(string prefix) =>
        Query.Where(note => note.Title.StartsWith(prefix)).OrderBy(note => note.Title);
}

/// <summary>One page of the notes matching a title prefix, ordered by title.</summary>
internal sealed class NotesPageSpec : Specification<Note>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="prefix">The title prefix to match.</param>
    /// <param name="skip">How many matches to skip.</param>
    /// <param name="take">How many matches to return.</param>
    public NotesPageSpec(string prefix, int skip, int take) =>
        Query.Where(note => note.Title.StartsWith(prefix)).OrderBy(note => note.Title).Skip(skip).Take(take);
}
