using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

/// <summary>One photograph in the player's album. The picture itself is a JPEG in the photos folder next to the saves.</summary>
public class PhotoInfo
{
    public string id;
    /// <summary>where and when it was taken, written under the picture</summary>
    public string caption;
    /// <summary>shown on the photo board at camp</summary>
    public bool pinned;
}

/// <summary>
/// The camera's photographs on disk: one JPEG per photo in a "photos" folder inside the saves folder, so Steam Cloud can carry
/// them with the saves. Saves only list the ids. Pictures are loaded once and kept as textures.
/// </summary>
public static class Photos
{
    /// <summary>How many photos one game can hold. The board itself shows <see cref="BOARD_SLOTS"/>.</summary>
    public const int MAX_PHOTOS = 60;
    public const int BOARD_SLOTS = 8;

    private static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
    /// <summary>where "Save to Pictures" writes instead of the real Pictures folder (the -autoshot smoke test)</summary>
    internal static string exportRoot;

    private static string Dir
    {
        get
        {
            var d = Path.Combine(Storage.Folder, "photos");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    private static string FileFor(string id)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) id = id.Replace(c, '_');
        return Path.Combine(Dir, id + ".jpg");
    }

    /// <summary>Stores a new picture and returns its id, or null when it could not be written.</summary>
    public static string save(Texture2D tex)
    {
        try
        {
            string id = "photo-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            byte[] jpg = tex.EncodeToJPG(90);
            string path = FileFor(id);
            cache[id] = tex;
            // the game does not stop to wait for the disk
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    File.WriteAllBytes(path, jpg);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("Could not write a photo: " + e.Message);
                }
            });
            return id;
        }
        catch (Exception e)
        {
            Debug.LogWarning("Could not save a photo: " + e.Message);
            return null;
        }
    }

    /// <summary>The picture for an id, or null if its file is missing.</summary>
    public static Texture2D get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (cache.TryGetValue(id, out var t) && t != null) return t;
        try
        {
            var f = FileFor(id);
            if (!File.Exists(f)) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false) { name = id, wrapMode = TextureWrapMode.Clamp };
            if (!tex.LoadImage(File.ReadAllBytes(f), true))
            {
                UnityEngine.Object.Destroy(tex);
                return null;
            }
            cache[id] = tex;
            return tex;
        }
        catch (Exception e)
        {
            Debug.LogWarning("Could not read a photo: " + e.Message);
            return null;
        }
    }

    /// <summary>
    /// Throws a photo away for this game. Its file goes too, unless a saved game still lists it (loading that save brings it
    /// back).
    /// </summary>
    public static void discard(string id)
    {
        if (cache.TryGetValue(id, out var t))
        {
            if (t != null) UnityEngine.Object.Destroy(t);
            cache.Remove(id);
        }
        string path = FileFor(id);
        string saves = Storage.Folder;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                foreach (var f in Directory.GetFiles(saves, "*.json"))
                    if (File.ReadAllText(f).Contains(id)) return;
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                /* a stray file does no harm */
            }
        });
    }

    /// <summary>
    /// Copies a photo to the player's own Pictures folder (in a "Cozy Treasure Hunter" folder), where it is easy to find and
    /// share. Returns the folder it went to, or null when it could not be written.
    /// </summary>
    public static string export(string id)
    {
        try
        {
            string pictures = exportRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (string.IsNullOrEmpty(pictures)) pictures = Application.persistentDataPath;
            string folder = Path.Combine(pictures, "Cozy Treasure Hunter");
            Directory.CreateDirectory(folder);
            string name = id.StartsWith("photo-") ? id.Substring(6) : id;
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            string target = Path.Combine(folder, $"Cozy Treasure Hunter {name}.jpg");
            string source = FileFor(id);
            if (File.Exists(source)) File.Copy(source, target, true);
            else if (cache.TryGetValue(id, out var tex) && tex != null && tex.isReadable) File.WriteAllBytes(target, tex.EncodeToJPG(90));
            else return null;
            return folder;
        }
        catch (Exception e)
        {
            Debug.LogWarning("Could not copy a photo to Pictures: " + e.Message);
            return null;
        }
    }

    /// <summary>Forgets the loaded pictures, so the next <see cref="get"/> reads them from disk (the -autoshot-photo test).</summary>
    internal static void clearCache()
    {
        foreach (var t in cache.Values) if (t != null) UnityEngine.Object.Destroy(t);
        cache.Clear();
    }
}
