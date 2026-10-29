using System;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Kids.Storage;

/// <summary>
/// The README.txt notes the plugin writes beside its data, so nobody takes the folders for leftovers (feature 14,
/// contracts/data-folder.md).
/// </summary>
internal static class DataFolderNotes
{
    /// <summary>
    /// The note's file name.
    /// </summary>
    public const string FileName = "README.txt";

    /// <summary>
    /// The note in the kids data folder.
    /// </summary>
    /// <param name="folder">The absolute data folder.</param>
    /// <returns>The text.</returns>
    public static string DataFolder(string folder) => $"""
        Jellyfin Kids plugin data
        =========================

        This folder holds the Jellyfin Kids plugin's data: kid profiles, the parent code
        (stored only as a verifier), screen time used, bonuses, live TV channels and picks.

        DO NOT DELETE THIS FOLDER. It is not a leftover: deleting it loses every kid's
        settings. Uninstalling or updating the plugin keeps it on purpose.

        Files:
          kids.db          the database (SQLite)
          kids.db-wal,
          kids.db-shm      SQLite's working files while Jellyfin runs; keep them with kids.db
          README.txt       this note, rewritten by the plugin

        Backing up:
          Jellyfin's own backup (Dashboard > Backups) does not include plugin data.
          Back up this folder with Jellyfin stopped, or, with sqlite3 installed, copy the
          database while Jellyfin runs:
            sqlite3 "{folder}/kids.db" ".backup kids-backup.db"
          Restore by stopping Jellyfin and putting kids.db back here (without old -wal/-shm
          files).

        To remove the plugin's data for good, uninstall the plugin, stop Jellyfin and delete
        this folder.

        Folder: {folder}
        More: https://github.com/aldoborrero/jellyfin-kids/blob/main/docs/install.md#your-data

        """;

    /// <summary>
    /// The note left in the old data folder after the move.
    /// </summary>
    /// <param name="folder">The new data folder.</param>
    /// <param name="renamedFile">The old database's new file name.</param>
    /// <returns>The text.</returns>
    public static string OldFolder(string folder, string renamedFile) => $"""
        Jellyfin Kids: old data folder
        ==============================

        Up to version 0.0.9.0 the Jellyfin Kids plugin kept its database here. Since
        0.0.10.0 it lives in:

          {folder}

        {renamedFile} is the old database, renamed and kept as a backup for one release.
        After checking that your kids and their settings are in Dashboard > Kids, you
        may delete this folder. A later version of the plugin removes it.

        """;

    /// <summary>
    /// Writes a note when it is missing or different. Never throws: a note is never a reason to refuse the data.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="text">The text.</param>
    /// <param name="logger">The logger.</param>
    public static void Write(string path, string text, ILogger logger)
    {
        try
        {
            if (File.Exists(path) && string.Equals(File.ReadAllText(path, Encoding.UTF8), text, StringComparison.Ordinal))
            {
                return;
            }

            File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Cannot write {Path}", path);
        }
    }
}
