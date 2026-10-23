using System.Reflection;

namespace SsSport.Drive.Api;

/// <summary>
/// Marker for the API host assembly, used for assembly scanning (DI, MediatR, AutoMapper) and architecture tests.
/// </summary>
public static class AssemblyReference
{
    /// <summary>Gets the API host assembly.</summary>
    public static Assembly Assembly { get; } = typeof(AssemblyReference).Assembly;
}
