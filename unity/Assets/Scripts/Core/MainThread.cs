using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

/// <summary>
/// Lets work on a background thread (the world being built while the loading screen moves, see Prebuilt) hand the few things
/// Unity only allows on the main thread, such as drawing text with a font, to the main thread, and wait for the answer.
/// On the main thread itself, a call simply runs.
/// </summary>
public static class MainThread
{
    private static int mainId = -1;
    private static readonly Queue<Action> queue = new Queue<Action>();
    private static readonly object gate = new object();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init() => mainId = Thread.CurrentThread.ManagedThreadId;

    public static bool IsMain => mainId < 0 || Thread.CurrentThread.ManagedThreadId == mainId;

    public static T Run<T>(Func<T> work)
    {
        if (IsMain) return work();
        T result = default;
        Exception failure = null;
        using (var done = new ManualResetEventSlim(false))
        {
            lock (gate)
            {
                queue.Enqueue(() =>
                {
                    try
                    {
                        result = work();
                    }
                    catch (Exception e)
                    {
                        failure = e;
                    }
                    finally
                    {
                        done.Set();
                    }
                });
            }
            done.Wait();
        }
        if (failure != null) throw new InvalidOperationException("Main thread work failed: " + failure.Message, failure);
        return result;
    }

    public static void Run(Action work) => Run(() =>
    {
        work();
        return true;
    });

    /// <summary>Runs what background threads have asked for. Called every frame by GameHost.</summary>
    public static void Pump()
    {
        while (true)
        {
            Action next;
            lock (gate)
            {
                if (queue.Count == 0) return;
                next = queue.Dequeue();
            }
            next();
        }
    }
}
