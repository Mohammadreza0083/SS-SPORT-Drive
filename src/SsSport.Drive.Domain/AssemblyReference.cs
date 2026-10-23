using System.Reflection;

namespace SsSport.Drive.Domain;

/// <summary>
/// Marker for the Domain layer assembly, used for assembly scanning (DI, MediatR, AutoMapper) and architecture tests.
/// </summary>
public static class AssemblyReference
{
    /// <summary>Gets the Domain layer assembly.</summary>
    public static Assembly Assembly { get; } = typeof(AssemblyReference).Assembly;
}
