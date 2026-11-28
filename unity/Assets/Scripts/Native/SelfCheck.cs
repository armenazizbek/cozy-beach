using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// -autoshot-selfcheck: checks the native building blocks against the Three.js code they replace, number by number, and logs
/// one `[SelfCheck] name: ok|FAIL ...` line each (unity/NATIVE_PLAN.md S2, S3). Every step that adds a block adds its checks
/// here; once the Three copy is gone (S25) the checks that need it go with it.
/// </summary>
internal static class SelfCheck
{
    internal static bool Requested => Array.IndexOf(Environment.GetCommandLineArgs(), "-autoshot-selfcheck") >= 0;

    private static readonly List<(string name, Func<string> run)> checks = new List<(string, Func<string>)>
    {
        ("GameSpace.Euler matches Quaternion.setFromEuler", EulerCheck),
        ("GameSpace.LookAt matches Object3D.lookAt", LookAtCheck),
        ("ColorUtil.Linear matches Color(hex)", ColorCheck),
        ("World root keeps game numbers", WorldCheck),
        ("DetectorTool.ComputeVibration scales with proximity and sensitivity", VibrationCheck),
    };

    /// <summary>Adds a check (MeshKit and later blocks register theirs from a static constructor or here).</summary>
    internal static void Add(string name, Func<string> run) => checks.Add((name, run));

    internal static int Run()
    {
        checks.AddRange(SelfCheckMeshKit.Checks());
        checks.AddRange(SelfCheckPicking.Checks());
        checks.AddRange(SelfCheckUi.Checks());
        int failed = 0;
        foreach (var (name, run) in checks)
        {
            string problem;
            try { problem = run(); }
            catch (Exception e) { problem = "threw " + e.GetType().Name + ": " + e.Message; }
            if (problem != null) failed++;
            Debug.Log($"[SelfCheck] {name}: {(problem == null ? "ok" : "FAIL " + problem)}");
        }
        Debug.Log($"[SelfCheck] {checks.Count - failed} of {checks.Count} ok");
        return failed;
    }

    private static readonly System.Random rnd = new System.Random(4242);
    private static double R(double a) => (rnd.NextDouble() * 2 - 1) * a;

    private static bool Same(Quaternion a, Quaternion b, float tol = 2e-6f)
    {
        // q and -q are the same rotation
        float d = Mathf.Abs(Quaternion.Dot(a, b));
        return 1 - d < tol;
    }

    private static string EulerCheck()
    {
        foreach (var order in new[] { "XYZ", "YXZ", "ZXY", "ZYX", "YZX", "XZY" })
            for (int i = 0; i < 200; i++)
            {
                double x = R(Math.PI), y = R(Math.PI), z = R(Math.PI);
                var three = new THREE.Quaternion().setFromEuler(new THREE.Euler(x, y, z, order));
                var t = new Quaternion((float)three._x, (float)three._y, (float)three._z, (float)three._w);
                var mine = GameSpace.Euler(x, y, z, order);
                if (!Same(t, mine)) return $"{order} ({x:0.###}, {y:0.###}, {z:0.###}): {t} vs {mine}";
            }
        return null;
    }

    private static string LookAtCheck()
    {
        for (int i = 0; i < 200; i++)
        {
            var from = new THREE.Vector3(R(50), R(10), R(50));
            var to = new THREE.Vector3(R(50), R(10), R(50));
            var o = new THREE.Object3D();
            o.position.copy(from);
            o.lookAt(to);
            var t = new Quaternion((float)o.quaternion._x, (float)o.quaternion._y, (float)o.quaternion._z, (float)o.quaternion._w);
            var mine = GameSpace.LookAt(V(from), V(to));
            if (!Same(t, mine, 1e-5f)) return $"object {from.x:0.#},{from.y:0.#},{from.z:0.#} -> {to.x:0.#},{to.y:0.#},{to.z:0.#}: {t} vs {mine}";
            var cam = new THREE.PerspectiveCamera(50, 1, 0.1, 100);
            cam.position.copy(from);
            cam.lookAt(to);
            var tc = new Quaternion((float)cam.quaternion._x, (float)cam.quaternion._y, (float)cam.quaternion._z, (float)cam.quaternion._w);
            var mc = GameSpace.LookAtAsCamera(V(from), V(to));
            if (!Same(tc, mc, 1e-5f)) return $"camera: {tc} vs {mc}";
        }
        return null;
    }

    private static string ColorCheck()
    {
        for (int i = 0; i < 2000; i++)
        {
            int hex = rnd.Next(0, 0x1000000);
            var t = new THREE.Color(hex);
            var c = ColorUtil.Linear(hex);
            if (Math.Abs(t.r - c.r) > 1e-6 || Math.Abs(t.g - c.g) > 1e-6 || Math.Abs(t.b - c.b) > 1e-6) return $"0x{hex:x6}";
        }
        return null;
    }

    private static string WorldCheck()
    {
        var root = World.Root;
        if (root == null) return "no world root";
        if (root.lossyScale.z > 0) return "the root is not mirrored";
        var probe = new GameObject("selfcheck probe").transform;
        try
        {
            probe.SetParent(root, false);
            probe.localPosition = new Vector3(3, 2, 7);
            // the bridge put the game point (3, 2, 7) at Unity (3, 2, -7); so must the world root
            var p = probe.position;
            if ((p - new Vector3(3, 2, -7)).sqrMagnitude > 1e-8f) return $"(3, 2, 7) landed at {p}";
            var back = GameSpace.WorldPos(probe);
            if ((back - new Vector3(3, 2, 7)).sqrMagnitude > 1e-8f) return $"WorldPos gave {back}";
            return null;
        }
        finally
        {
            UnityEngine.Object.Destroy(probe.gameObject);
        }
    }

    private static string VibrationCheck()
    {
        // no signal or zero sensitivity must stay silent
        var off = DetectorTool.ComputeVibration(0, 1, "generic", 1);
        if (off.low != 0f || off.high != 0f) return $"zero signal gave ({off.low}, {off.high})";
        var zeroSens = DetectorTool.ComputeVibration(1, 1, "coin", 0);
        if (zeroSens.low != 0f || zeroSens.high != 0f) return $"zero sensitivity gave ({zeroSens.low}, {zeroSens.high})";
        var nan = DetectorTool.ComputeVibration(double.NaN, double.NaN, null, double.NaN);
        if (nan.low != 0f || nan.high != 0f) return $"NaN gave ({nan.low}, {nan.high})";

        // closer to a find (higher strength) must vibrate strictly more across all signatures and sensitivities
        foreach (var kind in new[] { "generic", "coin", "deep", "trash" })
        {
            foreach (var sens in new[] { 0.25, 0.5, 1.0, 1.5, 2.0 })
            {
                float prevLo = 0f, prevHi = 0f;
                for (int i = 1; i <= 10; i++)
                {
                    double s = i / 10.0;
                    var (lo, hi) = DetectorTool.ComputeVibration(s, 0, kind, sens);
                    if (lo <= prevLo || hi <= prevHi) return $"non-monotonic proximity for {kind} @ sens {sens}, s={s}: ({prevLo},{prevHi}) -> ({lo},{hi})";
                    if (lo > 1f || hi > 1f) return $"out of range for {kind} @ sens {sens}, s={s}: ({lo},{hi})";
                    var (pLo, pHi) = DetectorTool.ComputeVibration(s, 1, kind, sens);
                    if (pLo <= lo || pHi <= hi) return $"ping pulse did not boost {kind} @ sens {sens}, s={s}: ({lo},{hi}) vs ({pLo},{pHi})";
                    if (pLo > 1f || pHi > 1f) return $"pulse out of range for {kind} @ sens {sens}, s={s}: ({pLo},{pHi})";
                    prevLo = lo;
                    prevHi = hi;
                }
            }
        }

        // higher sensitivity must increase vibration for the same proximity
        float sLo = 0f, sHi = 0f;
        foreach (var sens in new[] { 0.1, 0.5, 1.0, 1.5, 2.0 })
        {
            var (lo, hi) = DetectorTool.PreviewVibration(sens);
            if (lo <= sLo || hi <= sHi) return $"non-monotonic sensitivity @ {sens}: ({sLo},{sHi}) -> ({lo},{hi})";
            sLo = lo;
            sHi = hi;
        }

        // GamepadInput state tracking: setRumble, pulseRumble override, stopRumble reset
        var gp = new GamepadInput();
        try
        {
            gp.setRumble(0.4f, 0.6f);
            if (gp.currentRumble != (0.4f, 0.6f)) return $"setRumble state mismatch: {gp.currentRumble}";
            gp.pulseRumble(0.2f, 0.3f, 0.14);
            if (gp.currentRumble != (0.2f, 0.3f)) return $"pulseRumble override mismatch: {gp.currentRumble}";
            gp.stopRumble();
            if (gp.currentRumble != (0f, 0f)) return $"stopRumble did not clear: {gp.currentRumble}";
        }
        finally
        {
            gp.stopRumble();
        }
        return null;
    }

    private static Vector3 V(THREE.Vector3 v) => new Vector3((float)v.x, (float)v.y, (float)v.z);
}
