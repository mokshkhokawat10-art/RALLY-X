using System;
using System.IO;
using UnityEngine;

namespace RallyX.SaveSystem
{
    /// <summary>
    /// Reliable text files for saves. A save is written to a temporary file first and then swapped in, keeping the
    /// previous save as a backup (<c>.bak</c>), so a crash or power cut mid-save never leaves a half-written file,
    /// and a damaged save can fall back to the one before it.
    /// </summary>
    public static class SaveFile
    {
        public static string BackupPath(string path) => path + ".bak";

        private static string TemporaryPath(string path) => path + ".tmp";

        /// <summary>Writes <paramref name="text"/> to <paramref name="path"/>. Returns false (and logs why) if it couldn't.</summary>
        public static bool Write(string path, string text)
        {
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                string temporary = TemporaryPath(path);
                File.WriteAllText(temporary, text);
                if (File.Exists(path))
                    File.Replace(temporary, path, BackupPath(path));
                else
                    File.Move(temporary, path);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[RALLY X] Could not save {path}: {exception.Message}");
                return false;
            }
        }

        /// <summary>
        /// Reads the save, or its backup when the save is missing or <paramref name="parse"/> rejects it by throwing.
        /// Returns false when neither could be read.
        /// </summary>
        public static bool TryRead<T>(string path, Func<string, T> parse, out T result, out bool fromBackup)
        {
            fromBackup = false;
            foreach (string candidate in new[] { path, BackupPath(path) })
            {
                if (!File.Exists(candidate))
                    continue;

                try
                {
                    result = parse(File.ReadAllText(candidate));
                    fromBackup = candidate != path;
                    return true;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[RALLY X] Save file {candidate} could not be read: {exception.Message}");
                }
            }

            result = default;
            return false;
        }

        /// <summary>Deletes a save together with its backup and any leftover temporary file.</summary>
        public static void Delete(string path)
        {
            foreach (string file in new[] { path, BackupPath(path), TemporaryPath(path) })
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
        }
    }
}
