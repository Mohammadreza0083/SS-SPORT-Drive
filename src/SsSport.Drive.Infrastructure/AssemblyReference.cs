using System.Reflection;

namespace SsSport.Drive.Infrastructure;

/// <summary>
/// Marker for the Infrastructure layer assembly, used for assembly scanning (DI, MediatR, AutoMapper) and architecture tests.
/// </summary>
public static class AssemblyReference
{
    /// <summary>Gets the Infrastructure layer assembly.</summary>
    public static Assembly Assembly { get; } = typeof(AssemblyReference).Assembly;
}
