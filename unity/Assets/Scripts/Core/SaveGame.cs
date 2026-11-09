using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

// Port of src/core/SaveGame.ts. The JSON shape is unchanged, so saves from the browser and desktop builds load here too.

public class SavedItem
{
    public string defId;
    public double x, z, targetDepth;
    public bool collected;
}

public class PlayerPose
{
    public double x, z, yaw, pitch;
}

public class CastleSave
{
    public double x, z, stage, hp;
    public double? seed;
}

public class DogSave
{
    public int target;
}

public class VehicleSave
{
    public double x, z, heading;
}

public class XZ
{
    public double x, z;
}

/// <summary>Everything needed to put the world back exactly as the player left it.</summary>
public class Snapshot
{
    public int v = 1;
    public double savedAt;
    public GameData data;
    public PlayerPose player;
    public int tool;
    public bool radio;
    /// <summary>which radio station was playing</summary>
    public int? radioTrack;
    /// <summary>time of day in [0,1)</summary>
    public double? dayTime;
    /// <summary>the beach resolution it was saved on; holes from another map size cannot be reused</summary>
    public int? size;
    /// <summary>where the player dug, for the map</summary>
    public List<double[]> holes;
    public XZ waypoint;
    /// <summary>planted marker flags</summary>
    public List<double[]> flags;
    /// <summary>sand castles on the beach, with how worn down they are</summary>
    public List<CastleSave> castles;
    /// <summary>the find the dog is working on: its index in the item list, or -1</summary>
    public DogSave dog;
    public VehicleSave cart;
    public VehicleSave boat;
    /// <summary>which sea chests have already been hauled in</summary>
    public List<bool> chests;
    public List<SavedItem> items = new List<SavedItem>();
    /// <summary>sparse list of [vertex index, dug depth] for every changed sand vertex</summary>
    public List<double[]> dug = new List<double[]>();
}

/// <summary>The small summary shown in the save list; the heavy snapshot lives under its own key.</summary>
public class SaveMeta
{
    public string id, name;
    public double savedAt, coins, finds;
}

public static class SaveGame
{
    private const string INDEX_KEY = "cozy-treasure-hunter-saves-v3";
    private const string SLOT_PREFIX = "cozy-treasure-hunter-slot-";
    private const string OLD_KEY = "cozy-treasure-hunter-save-v2";

    /// <summary>The one slot the game saves into by itself; every autosave writes over it.</summary>
    public const string AUTOSAVE_ID = "autosave";

    public static readonly JsonSerializerSettings Json = new JsonSerializerSettings
    {
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        NullValueHandling = NullValueHandling.Ignore,
        MissingMemberHandling = MissingMemberHandling.Ignore,
    };

    private static List<SaveMeta> readIndex()
    {
        try
        {
            var raw = Storage.getItem(INDEX_KEY);
            return raw != null ? JsonConvert.DeserializeObject<List<SaveMeta>>(raw, Json) ?? new List<SaveMeta>() : new List<SaveMeta>();
        }
        catch
        {
            return new List<SaveMeta>();
        }
    }

    private static bool writeIndex(List<SaveMeta> list) => Storage.setItem(INDEX_KEY, JsonConvert.SerializeObject(list, Json));

    /// <summary>Brings the earlier single-slot save along, once.</summary>
    private static void migrateOld()
    {
        try
        {
            var raw = Storage.getItem(OLD_KEY);
            if (raw == null) return;
            var snap = JsonConvert.DeserializeObject<Snapshot>(raw, Json);
            Storage.removeItem(OLD_KEY);
            if (snap != null && snap.v == 1) writeSave(snap, "Earlier save");
        }
        catch
        {
            /* ignore */
        }
    }

    /// <summary>All saves, newest first.</summary>
    public static List<SaveMeta> listSaves()
    {
        migrateOld();
        return readIndex().OrderByDescending(m => m.savedAt).ToList();
    }

    public static Snapshot readSave(string id)
    {
        try
        {
            var raw = Storage.getItem(SLOT_PREFIX + id);
            if (raw == null)
            {
                UnityEngine.Debug.LogWarning($"Save '{id}': its data file is missing ({SLOT_PREFIX + id}.json).");
                return null;
            }
            var s = JsonConvert.DeserializeObject<Snapshot>(raw, Json);
            if (s == null || s.v != 1) UnityEngine.Debug.LogWarning($"Save '{id}': not a version 1 save.");
            return s != null && s.v == 1 ? s : null;
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning($"Save '{id}' could not be read: {e.Message}");
            return null;
        }
    }

    /// <summary>True when the last load that was asked for found nothing it could use; the game says so instead of quietly starting fresh.</summary>
    public static bool lastLoadFailed { get; private set; }

    private static string Base36(long n)
    {
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        if (n == 0) return "0";
        var s = "";
        while (n > 0) { s = digits[(int)(n % 36)] + s; n /= 36; }
        return s;
    }

    /// <summary>Saves a snapshot as a new entry, or over the entry `id`. Returns null if storage refused.</summary>
    public static SaveMeta writeSave(Snapshot snap, string name, string id = null)
    {
        var meta = new SaveMeta
        {
            id = id ?? Base36((long)JS.DateNow()) + Base36((long)Math.Floor(JS.Random() * 1e4)),
            name = string.IsNullOrWhiteSpace(name) ? "Unnamed save" : name.Trim(),
            savedAt = snap.savedAt,
            coins = snap.data.coins,
            finds = snap.data.found.Count,
        };
        if (!Storage.setItem(SLOT_PREFIX + meta.id, JsonConvert.SerializeObject(snap, Json))) return null;
        var list = readIndex().Where(m => m.id != meta.id).ToList();
        list.Add(meta);
        return writeIndex(list) ? meta : null;
    }

    public static void deleteSave(string id)
    {
        Storage.removeItem(SLOT_PREFIX + id);
        writeIndex(readIndex().Where(m => m.id != id).ToList());
    }

    /// <summary>Loading swaps the whole world, so we remember which save and rebuild the game into it.</summary>
    private static string pendingLoad;

    public static void requestLoad(string id) => pendingLoad = id;

    /// <summary>Returns the save to start from, if the player asked to load one before the game was rebuilt.</summary>
    public static Snapshot takePendingLoad()
    {
        var id = pendingLoad;
        pendingLoad = null;
        var snap = id != null ? readSave(id) : null;
        lastLoadFailed = id != null && snap == null;
        return snap;
    }

    public static string timeLabel(double t)
    {
        var d = DateTimeOffset.FromUnixTimeMilliseconds((long)t).LocalDateTime;
        return d.ToString("MMM d, HH:mm", System.Globalization.CultureInfo.CurrentCulture);
    }
}
