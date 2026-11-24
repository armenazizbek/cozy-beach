using THREE;
// inside THREE these names meant its own types; kept so after the move to NativeRender
using Color = THREE.Color;
using Vector2 = THREE.Vector2;
using Vector3 = THREE.Vector3;
using Vector4 = THREE.Vector4;
using Quaternion = THREE.Quaternion;
using Camera = THREE.Camera;
using Texture = THREE.Texture;
using Material = THREE.Material;
using Mesh = THREE.Mesh;
using Light = THREE.Light;
using Sprite = THREE.Sprite;
using Plane = THREE.Plane;
using Ray = THREE.Ray;
using ColorSpace = THREE.ColorSpace;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace NativeRender
{
    /// <summary>
    /// Cheap timings of the bridge's own work (the game's frame, copying the scene into Unity, rebuilding meshes), read by the
    /// -autoshot smoke test to find what a slow frame spends its time on. Only timestamps and counters: nothing is logged here.
    /// </summary>
    public static class FrameStats
    {
        public static long Ticks() => Stopwatch.GetTimestamp();
        public static double Ms(long since) => (Stopwatch.GetTimestamp() - since) * 1000.0 / Stopwatch.Frequency;

        public static int frames;
        /// <summary>Objects walked by the scene sync, and how many of them draw something (meshes, points, lines, sprites).</summary>
        public static long objects, drawables, composed, pushed;
        /// <summary>static groups (Object3D.syncStatic) skipped by the scene copy, and copied again because something in them was touched</summary>
        public static long staticSkipped, staticSynced;
        public static double tickMs, syncMs, buildMs;
        public static int builds;
        public static long verts;
        private static readonly Dictionary<string, (int n, long verts, double ms)> byMesh = new Dictionary<string, (int, long, double)>();

        /// <summary>Objects the scene copy visited under each top-level object of the scene (by its index there).</summary>
        private static readonly Dictionary<int, (string name, long n)> byRoot = new Dictionary<int, (string, long)>();

        public static void CountRoot(int index, Object3D o, long n)
        {
            byRoot.TryGetValue(index, out var t);
            byRoot[index] = (string.IsNullOrEmpty(o.name) ? o.type : o.name, t.n + n);
        }

        public static string RootReport(int count = 16)
        {
            int f = System.Math.Max(1, frames);
            return string.Join(", ", byRoot.OrderByDescending(kv => kv.Value.n).Take(count).Select(kv => $"{kv.Key}:{kv.Value.name} {kv.Value.n / f}"));
        }

        public static void Reset()
        {
            byRoot.Clear();
            frames = builds = 0;
            tickMs = syncMs = buildMs = 0;
            verts = objects = drawables = composed = pushed = staticSkipped = staticSynced = 0;
            byMesh.Clear();
            sections.Clear();
            garbage.Clear();
        }
        /// <summary>the bytes each named part allocated on the main thread (the garbage collector's later work)</summary>
        private static readonly Dictionary<string, long> garbage = new Dictionary<string, long>();
        /// <summary>
        /// Bytes allocated so far: .NET's own count where the runtime keeps one; otherwise (Unity's Mono) the heap in use,
        /// which grows as garbage is made, in steps of a few KB, and drops at a collection (only rises are counted).
        /// </summary>
        public static long Allocated() => exact ? System.GC.GetAllocatedBytesForCurrentThread() : System.GC.GetTotalMemory(false);
        private static readonly bool exact = System.GC.GetAllocatedBytesForCurrentThread() > 0;

        private static readonly Dictionary<string, double> sections = new Dictionary<string, double>();

        /// <summary>Times a named part of the game's frame: <c>using (FrameStats.Time("dog")) updateDog(dt, t);</c></summary>
        public static Section Time(string name) => new Section(name);

        public readonly struct Section : System.IDisposable
        {
            private readonly string name;
            private readonly long t0, a0;
            public Section(string name) { this.name = name; t0 = Ticks(); a0 = Allocated(); }
            public void Dispose()
            {
                long bytes = Allocated() - a0;
                if (bytes > 0)
                {
                    garbage.TryGetValue(name, out var b);
                    garbage[name] = b + bytes;
                }
                sections.TryGetValue(name, out var ms);
                sections[name] = ms + Ms(t0);
            }
        }

        /// <summary>The named parts of the frame that allocate the most, in bytes per frame (empty where nothing is counted).</summary>
        public static string GarbageReport(int count = 14)
        {
            int f = System.Math.Max(1, frames);
            return string.Join(", ", garbage.OrderByDescending(kv => kv.Value).Take(count).Select(kv => $"{kv.Key} {kv.Value / f}"));
        }

        /// <summary>The slowest named parts of the game's frame, per frame.</summary>
        public static string SectionReport(int count = 14)
        {
            int f = System.Math.Max(1, frames);
            return string.Join(", ", sections.OrderByDescending(kv => kv.Value).Take(count).Select(kv => $"{kv.Key} {kv.Value / f:0.00}"));
        }

        public static void Built(string what, int vertexCount, double ms)
        {
            builds++;
            verts += vertexCount;
            buildMs += ms;
            byMesh.TryGetValue(what, out var t);
            byMesh[what] = (t.n + 1, t.verts + vertexCount, t.ms + ms);
        }

        /// <summary>One line per frame average, plus the meshes that cost the most to rebuild.</summary>
        public static string Report()
        {
            int f = System.Math.Max(1, frames);
            var top = byMesh.OrderByDescending(kv => kv.Value.ms).Take(6)
                .Select(kv => $"{kv.Key} x{kv.Value.n / (double)f:0.##}/frame {kv.Value.ms / f:0.00}ms {kv.Value.verts / f} verts");
            return $"game+sync {tickMs / f:0.00} ms, of it scene sync {syncMs / f:0.00} ms ({objects / f} objects, {drawables / f} drawn, {composed / f} matrices recomposed, {pushed / f} transforms pushed, static groups {staticSkipped / f} skipped and {staticSynced / f} copied), mesh rebuilds {builds / (double)f:0.##}/frame " +
                   $"{buildMs / f:0.00} ms {verts / f} verts; top: {string.Join("; ", top)}; by root: {RootReport()}";
        }
    }
}
