using System.Threading;
using UnityEngine;

/// <summary>
/// The clock the game reads (JS.Now, the page's timers and animations, the smoke test's waits). Normally it is real time.
/// With -autoshot-fixedstep (smoke test only, see unity/NATIVE_PLAN.md section 4) it moves on by exactly 1/60 s per frame,
/// so what is on screen at each screenshot depends only on how many frames have passed, not on how fast the PC draws:
/// two runs of the same build, or of two builds, can then be compared picture by picture.
/// </summary>
public static class TestClock
{
    public const double STEP = 1.0 / 60;

    /// <summary>True when the clock is stepped per frame instead of following real time.</summary>
    public static bool Fixed { get; private set; }

    private static long frames;

    /// <summary>Seconds: real time since start-up, or frames x 1/60 when fixed. Safe to read from any thread.</summary>
    public static double Seconds => Fixed ? Interlocked.Read(ref frames) * STEP : Time.realtimeSinceStartupAsDouble;

    /// <summary>Milliseconds, like performance.now().</summary>
    public static double Ms => Seconds * 1000.0;

    /// <summary>Switches to the stepped clock for the rest of the run.</summary>
    internal static void UseFixedStep()
    {
        if (Fixed) return;
        Fixed = true;
        // Unity's own frame time too, for anything that reads Time.deltaTime
        Time.captureDeltaTime = (float)STEP;
        var go = new GameObject("TestClock");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<TestClockDriver>();
        // a run that can be repeated takes no input but the smoke test's own: a real mouse turned the view in one run and
        // not the next, and a click elsewhere paused the game (see GameHost.OnApplicationFocus)
        foreach (var d in UnityEngine.InputSystem.InputSystem.devices) Isolate(d);
        UnityEngine.InputSystem.InputSystem.onDeviceChange += (d, change) =>
        {
            if (change == UnityEngine.InputSystem.InputDeviceChange.Added || change == UnityEngine.InputSystem.InputDeviceChange.Reconnected) Isolate(d);
        };
    }

    /// <summary>Switches off a real mouse or keyboard (virtual controllers the smoke test adds stay on).</summary>
    private static void Isolate(UnityEngine.InputSystem.InputDevice d)
    {
        if ((d is UnityEngine.InputSystem.Mouse || d is UnityEngine.InputSystem.Keyboard || d is UnityEngine.InputSystem.Pen
             || d is UnityEngine.InputSystem.Touchscreen) && d.enabled)
            UnityEngine.InputSystem.InputSystem.DisableDevice(d);
    }

    /// <summary>
    /// While true the stepped clock stands still. Prebuilt sets it while it waits for its worker thread: how many frames
    /// that takes depends on the PC, and counting them would put every later moment of the run at a different time.
    /// </summary>
    internal static volatile bool Held;

    internal static void Tick()
    {
        if (!Held) Interlocked.Increment(ref frames);
    }
}

/// <summary>Moves the stepped clock on once per frame, before any other script runs.</summary>
[DefaultExecutionOrder(-32000)]
internal class TestClockDriver : MonoBehaviour
{
    private void Update() => TestClock.Tick();
}
