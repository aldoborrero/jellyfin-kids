using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.Kids.Tests;

/// <summary>
/// A unique folder under the system temp dir, deleted on dispose.
/// </summary>
public sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jellyfin-kids-tests", Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    /// Gets the folder path. The folder itself is not created.
    /// </summary>
    public string Path { get; }

    public void Dispose()
    {
        // Release pooled handles from tests that open the file directly, so the folder can be deleted
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
