using Jellyfin.Plugin.Kids.Storage;

namespace Jellyfin.Plugin.Kids.Api;

/// <summary>
/// Where the plugin keeps its data, for the dashboard (feature 14). Administrators only: a server path is never
/// shown anonymously.
/// </summary>
/// <param name="Folder">The kids data folder.</param>
/// <param name="DatabasePath">The database file.</param>
/// <param name="Available">Whether the database could be opened (false after a refused or failed move).</param>
public record StorageInfoDto(string Folder, string DatabasePath, bool Available)
{
    /// <summary>
    /// Reads the database's location and state.
    /// </summary>
    /// <param name="database">The database.</param>
    /// <returns>The DTO.</returns>
    public static StorageInfoDto From(KidsDatabase database)
    {
        System.ArgumentNullException.ThrowIfNull(database);
        return new StorageInfoDto(database.Folder, database.DatabasePath, database.EnsureInitialized());
    }
}
