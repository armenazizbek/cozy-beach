using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace THREE.Bridge
{
    /// <summary>
    /// Cheap timings of the bridge's own work (the game's frame, copying the scene into Unity, rebuilding meshes), read by the
    /// -autoshot smoke test to find what a slow frame spends its time on. Only timestamps and counters: nothing is logged here.
    /// </summary>
    public static class BridgeStats
    {
        public static long Ticks() => Stopwatch.GetTimestamp();
        public static double Ms(long since) => (Stopwatch.GetTimestamp() - since) * 1000.0 / Stopwatch.Frequency;

        public static int frames;
        /// <summary>Objects walked by the scene sync, and how many of them draw something (meshes, points, lines, sprites).</summary>
        public static long objects, drawables, composed, pushed;
        public static double tickMs, syncMs, buildMs;
        public static int builds;
        public static long verts;
        private static readonly Dictionary<string, (int n, long verts, double ms)> byMesh = new Dictionary<string, (int, long, double)>();

        public static void Reset()
        {
            frames = builds = 0;
            tickMs = syncMs = buildMs = 0;
            verts = objects = drawables = composed = pushed = 0;
            byMesh.Clear();
            sections.Clear();
        }

        private static readonly Dictionary<string, double> sections = new Dictionary<string, double>();

        /// <summary>Times a named part of the game's frame: <c>using (BridgeStats.Time("dog")) updateDog(dt, t);</c></summary>
        public static Section Time(string name) => new Section(name);

        public readonly struct Section : System.IDisposable
        {
            private readonly string name;
            private readonly long t0;
            public Section(string name) { this.name = name; t0 = Ticks(); }
            public void Dispose()
            {
                sections.TryGetValue(name, out var ms);
                sections[name] = ms + Ms(t0);
            }
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
            return $"game+sync {tickMs / f:0.00} ms, of it scene sync {syncMs / f:0.00} ms ({objects / f} objects, {drawables / f} drawn, {composed / f} matrices recomposed, {pushed / f} transforms pushed), mesh rebuilds {builds / (double)f:0.##}/frame " +
                   $"{buildMs / f:0.00} ms {verts / f} verts; top: {string.Join("; ", top)}";
        }
    }
}
