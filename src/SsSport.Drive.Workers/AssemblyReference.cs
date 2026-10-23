using System.Reflection;

namespace SsSport.Drive.Workers;

/// <summary>
/// Marker for the worker host assembly, used for assembly scanning (DI, MediatR, AutoMapper) and architecture tests.
/// </summary>
public static class AssemblyReference
{
    /// <summary>Gets the worker host assembly.</summary>
    public static Assembly Assembly { get; } = typeof(AssemblyReference).Assembly;
}
