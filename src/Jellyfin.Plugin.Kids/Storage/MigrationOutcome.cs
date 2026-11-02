namespace Jellyfin.Plugin.Kids.Storage;

/// <summary>
/// What the move of the kids data from the old folder did (feature 14).
/// </summary>
public enum MigrationOutcome
{
    /// <summary>No database at the old place (a fresh install, or already moved); nothing changed.</summary>
    NothingToDo,

    /// <summary>The old database was copied, verified and put in place, and the old file renamed.</summary>
    Migrated,

    /// <summary>Both held the same data (an interrupted move); the old file was renamed.</summary>
    Finished,

    /// <summary>Both existed and the new one was written later; the old file was renamed aside.</summary>
    KeptNew,

    /// <summary>Both existed and the plugin could not tell which to keep; nothing was touched.</summary>
    Refused,

    /// <summary>The old database could not be read, copied or verified; it is untouched and nothing was created.</summary>
    Failed
}
