using Riok.Mapperly.Abstractions;
using SsSport.Drive.Domain.DriveFolders;

namespace SsSport.Drive.Application.DriveFolders;

/// <summary>Maps <see cref="DriveFolder"/> to <see cref="DriveFolderDto"/> at compile time (Mapperly).</summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class DriveFolderMapper
{
    /// <summary>Maps a folder to its DTO.</summary>
    /// <param name="folder">The folder.</param>
    /// <returns>The DTO.</returns>
    public static partial DriveFolderDto ToDto(this DriveFolder folder);
}
