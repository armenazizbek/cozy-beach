using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// The biggest pieces of the world, made on a background thread before the game itself is built, so the main thread stays free
/// to draw the loading screen and it moves smoothly. The 3D pieces are plain C# until they are first drawn; the few Unity calls
/// they make (text on signs, pictures from Resources) are handed to the main thread (MainThread). A piece that fails off the
/// main thread is made again on it afterwards. Game takes each one as it builds its fields; anything not made ahead is made on
/// the spot as before.
/// </summary>
internal static class Prebuilt
{
    private static readonly Dictionary<Type, object> made = new Dictionary<Type, object>();
    private static int done;

    // in the order Game's fields make them, so each is built in the same order as without this
    private static readonly (Type type, Func<object> build)[] PIECES =
    {
        (typeof(Beach), () => new Beach()),
        (typeof(Camp), () => new Camp()),
        (typeof(Scenery), () => new Scenery()),
        // the map is drawn from the scenery just made: the same one the game takes
        (typeof(MapView), () => { var s = Peek<Scenery>(); return new MapView(s.trunks, s.mountains); }),
        (typeof(SeaLife), () => new SeaLife()),
        (typeof(Island[]), () => Beach.ISLANDS.Select(s => new Island(s)).ToArray()),
        (typeof(Underwater), () => new Underwater()),
        (typeof(Underground), () => new Underground()),
        (typeof(OldHouse), () => new OldHouse()),
        (typeof(ViewModel), () => new ViewModel()),
    };

    /// <summary>How far the pieces are, 0..1, for the loading bar.</summary>
    internal static float Progress => (float)Volatile.Read(ref done) / PIECES.Length;

    internal static IEnumerator MakeAll()
    {
        lock (made) made.Clear();
        done = 0;
        var failed = new List<int>();
        var work = Task.Run(() =>
        {
            for (int i = 0; i < PIECES.Length; i++)
            {
                try
                {
                    var piece = PIECES[i].build();
                    lock (made) made[PIECES[i].type] = piece;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Prebuilt: {PIECES[i].type.Name} could not be made off the main thread, so it is made on it: {e.Message}");
                    lock (failed) failed.Add(i);
                }
                Interlocked.Increment(ref done);
            }
        });
        while (!work.IsCompleted) yield return null;
        foreach (int i in failed)
        {
            var piece = PIECES[i].build();
            lock (made) made[PIECES[i].type] = piece;
            yield return null;
        }
    }

    private static T Peek<T>()
    {
        lock (made) return (T)made[typeof(T)];
    }

    internal static T Take<T>(Func<T> build)
    {
        lock (made)
        {
            if (made.TryGetValue(typeof(T), out var o))
            {
                made.Remove(typeof(T));
                return (T)o;
            }
        }
        return build();
    }
}
