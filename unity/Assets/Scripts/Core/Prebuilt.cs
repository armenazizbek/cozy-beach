using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// The biggest pieces of the world, made on background threads, all at the same time, before the game itself is built, so
/// loading takes as long as the slowest piece rather than all of them in turn, and the main thread stays free to draw the
/// loading screen. The 3D library's scratch objects are per thread for this (THREE's [ThreadStatic] fields). The 3D pieces are plain C# until they are first drawn; the few Unity calls
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
        // every piece is made at the same time on the thread pool, each with random numbers of its own (the test seed's own
        // sequence for that piece under -autoshot-seed, as before; otherwise a seed drawn here, so no two threads share one
        // generator); only the map waits, for the scenery it is drawn from
        var seeds = new int[PIECES.Length];
        for (int i = 0; i < seeds.Length; i++) seeds[i] = (int)(JS.Random() * int.MaxValue);
        void make(int i)
        {
            try
            {
                if (JS.TestSeed is int s) JS.SeedThread(s * 1000 + i);
                else JS.SeedThread(seeds[i]);
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                var piece = PIECES[i].build();
                lock (made) made[PIECES[i].type] = piece;
                AutoShot.TraceOff($"piece {PIECES[i].type.Name}: {(System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0} ms");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Prebuilt: {PIECES[i].type.Name} could not be made off the main thread, so it is made on it: {e.Message}");
                lock (failed) failed.Add(i);
            }
            finally
            {
                JS.SeedThread(null);
                Interlocked.Increment(ref done);
            }
        }
        var tasks = new Task[PIECES.Length];
        int scenery = Array.FindIndex(PIECES, p => p.type == typeof(Scenery));
        for (int i = 0; i < PIECES.Length; i++)
        {
            int k = i;
            if (PIECES[k].type == typeof(MapView)) continue;
            tasks[k] = Task.Run(() => make(k));
        }
        int map = Array.FindIndex(PIECES, p => p.type == typeof(MapView));
        tasks[map] = tasks[scenery].ContinueWith(_ => make(map), TaskScheduler.Default);
        var work = Task.WhenAll(tasks);
        // -autoshot-fixedstep: the frames spent waiting here are not counted (see TestClock.Held)
        TestClock.Held = TestClock.Fixed;
        try
        {
            while (!work.IsCompleted) yield return null;
        }
        finally
        {
            TestClock.Held = false;
        }
        // in their own order, so a map whose scenery failed is made after it
        failed.Sort();
        foreach (int i in failed)
        {
            SeedFor(i);
            var piece = PIECES[i].build();
            JS.SeedThread(null);
            lock (made) made[PIECES[i].type] = piece;
            yield return null;
        }
    }

    /// <summary>
    /// Under -autoshot-seed each piece draws from a sequence of its own, so it gets the same numbers whichever thread makes it
    /// and whatever the main thread draws meanwhile. Without the flag nothing changes.
    /// </summary>
    private static void SeedFor(int piece)
    {
        if (JS.TestSeed is int s) JS.SeedThread(s * 1000 + piece);
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
