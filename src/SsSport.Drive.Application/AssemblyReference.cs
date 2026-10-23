using System.Reflection;

namespace SsSport.Drive.Application;

/// <summary>
/// Marker for the Application layer assembly, used for assembly scanning (DI, MediatR, AutoMapper) and architecture tests.
/// </summary>
public static class AssemblyReference
{
    /// <summary>Gets the Application layer assembly.</summary>
    public static Assembly Assembly { get; } = typeof(AssemblyReference).Assembly;
}
