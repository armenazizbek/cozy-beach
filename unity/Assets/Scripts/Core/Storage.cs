using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

/// <summary>
/// Port of src/core/Storage.ts. Saves and achievements are plain JSON files in the game's data folder (one file per key, as the
/// desktop app kept them), which Steam can copy to the cloud. The rest of the game just uses getItem / setItem.
/// </summary>
public static class Storage
{
    private static string dir;
    private static readonly object gate = new object();

    private static string Dir
    {
        get
        {
            if (dir != null) return dir;
            dir = Path.Combine(Application.persistentDataPath, "saves");
            Directory.CreateDirectory(dir);
            ImportDesktopSaves(dir);
            return dir;
        }
    }

    /// <summary>Keeps saves in another folder for this run (the -autoshot smoke test, so it never touches real saves).</summary>
    internal static void UseFolder(string folder)
    {
        lock (gate)
        {
            dir = folder;
            Directory.CreateDirectory(dir);
        }
    }

    private static string FileFor(string key)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) key = key.Replace(c, '_');
        return Path.Combine(Dir, key + ".json");
    }

    public static string getItem(string key)
    {
        try
        {
            lock (gate)
            {
                var f = FileFor(key);
                return File.Exists(f) ? File.ReadAllText(f) : null;
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Returns false when the value could not be written (a full disk, a read-only folder).</summary>
    public static bool setItem(string key, string value)
    {
        try
        {
            lock (gate)
            {
                var f = FileFor(key);
                var tmp = f + ".tmp";
                File.WriteAllText(tmp, value);
                if (File.Exists(f)) File.Replace(tmp, f, null);
                else File.Move(tmp, f);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>For small things saved in the middle of play: the game does not stop to wait for the disk.</summary>
    public static void setItemLater(string key, string value)
    {
        ThreadPool.QueueUserWorkItem(_ => setItem(key, value));
    }

    public static void removeItem(string key)
    {
        try
        {
            lock (gate)
            {
                var f = FileFor(key);
                if (File.Exists(f)) File.Delete(f);
            }
        }
        catch
        {
            /* ignore */
        }
    }

    /// <summary>
    /// The first time the Unity build runs, it brings across the saves and achievements of the Electron desktop app, so nobody loses
    /// their beach in the move.
    /// </summary>
    private static void ImportDesktopSaves(string target)
    {
        try
        {
            if (Directory.GetFiles(target, "*.json").Length > 0) return;
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var candidates = new List<string>
            {
                Path.Combine(roaming, "Cozy Treasure Hunter", "saves"),
                Path.Combine(roaming, "cozy-treasure-hunter", "saves"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "Application Support", "Cozy Treasure Hunter", "saves"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), ".config", "Cozy Treasure Hunter", "saves"),
            };
            foreach (var c in candidates)
            {
                if (!Directory.Exists(c)) continue;
                foreach (var f in Directory.GetFiles(c, "*.json")) File.Copy(f, Path.Combine(target, Path.GetFileName(f)), false);
                Debug.Log($"Brought saves across from {c}");
                return;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("Could not bring old saves across: " + e.Message);
        }
    }
}

/// <summary>What the desktop app offered for Steam. Steamworks.NET can be wired in here; until then it does nothing.</summary>
public static class SteamBridge
{
    public static Action<string> unlockHandler;

    public static void Unlock(string id) => unlockHandler?.Invoke(id);
}
