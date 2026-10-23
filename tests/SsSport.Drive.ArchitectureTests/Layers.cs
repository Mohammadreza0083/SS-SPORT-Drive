using System.Reflection;

namespace SsSport.Drive.ArchitectureTests;

/// <summary>
/// Assemblies and root namespaces of every layer, shared by the architecture tests.
/// </summary>
internal static class Layers
{
    /// <summary>Root namespace of the Domain layer.</summary>
    public const string DomainNamespace = "SsSport.Drive.Domain";

    /// <summary>Root namespace of the Application layer.</summary>
    public const string ApplicationNamespace = "SsSport.Drive.Application";

    /// <summary>Root namespace of the Infrastructure layer.</summary>
    public const string InfrastructureNamespace = "SsSport.Drive.Infrastructure";

    /// <summary>Root namespace of the API host.</summary>
    public const string ApiNamespace = "SsSport.Drive.Api";

    /// <summary>Root namespace of the worker host.</summary>
    public const string WorkersNamespace = "SsSport.Drive.Workers";

    /// <summary>The Domain assembly.</summary>
    public static Assembly Domain => Drive.Domain.AssemblyReference.Assembly;

    /// <summary>The Application assembly.</summary>
    public static Assembly Application => Drive.Application.AssemblyReference.Assembly;

    /// <summary>The Infrastructure assembly.</summary>
    public static Assembly Infrastructure => Drive.Infrastructure.AssemblyReference.Assembly;

    /// <summary>The API host assembly.</summary>
    public static Assembly Api => Drive.Api.AssemblyReference.Assembly;

    /// <summary>The worker host assembly.</summary>
    public static Assembly Workers => Drive.Workers.AssemblyReference.Assembly;
}
