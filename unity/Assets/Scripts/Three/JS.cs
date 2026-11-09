using System;
using System.Collections.Generic;

/// <summary>
/// JavaScript behaviours that C# does not share, so ported code keeps the same numbers.
/// </summary>
public static class JS
{
    private static Random rng = new Random();

    /// <summary>Math.random(): a number in [0, 1).</summary>
    public static double Random() => rng.NextDouble();

    /// <summary>Restarts the random sequence (handy for tests). The game itself never seeds it, like the browser.</summary>
    public static void Seed(int seed) => rng = new Random(seed);

    /// <summary>Math.round(): halves round up (toward +infinity), unlike Math.Round's banker's rounding.</summary>
    public static double Round(double x) => Math.Floor(x + 0.5);

    public static int RoundInt(double x) => (int)Math.Floor(x + 0.5);

    /// <summary>ToInt32, as the bitwise operators apply it: wraps modulo 2^32 into a signed 32-bit value.</summary>
    public static int ToInt32(double x)
    {
        if (double.IsNaN(x) || double.IsInfinity(x)) return 0;
        double t = Math.Truncate(x) % 4294967296.0;
        if (t < 0) t += 4294967296.0;
        return unchecked((int)(uint)t);
    }

    /// <summary>ToUint32 (the result of `x >>> 0`).</summary>
    public static uint ToUint32(double x) => unchecked((uint)ToInt32(x));

    /// <summary>Math.imul(): the low 32 bits of a 32-bit integer product.</summary>
    public static int Imul(double a, double b) => unchecked(ToInt32(a) * ToInt32(b));

    /// <summary>Math.sign(): -1, 0 or 1, and NaN stays NaN.</summary>
    public static double Sign(double x) => double.IsNaN(x) ? double.NaN : x > 0 ? 1 : x < 0 ? -1 : 0;

    public static double Hypot(double a, double b) => Math.Sqrt(a * a + b * b);

    public static double Hypot(double a, double b, double c) => Math.Sqrt(a * a + b * b + c * c);

    public static double Hypot(params double[] v)
    {
        double s = 0;
        foreach (var x in v) s += x * x;
        return Math.Sqrt(s);
    }

    public static double Max(double a, double b) => double.IsNaN(a) || double.IsNaN(b) ? double.NaN : Math.Max(a, b);

    public static double Max(double a, double b, double c) => Max(Max(a, b), c);

    public static double Max(params double[] v)
    {
        double m = double.NegativeInfinity;
        foreach (var x in v)
        {
            if (double.IsNaN(x)) return double.NaN;
            if (x > m) m = x;
        }
        return m;
    }

    public static double Min(double a, double b) => double.IsNaN(a) || double.IsNaN(b) ? double.NaN : Math.Min(a, b);

    public static double Min(double a, double b, double c) => Min(Min(a, b), c);

    public static double Min(params double[] v)
    {
        double m = double.PositiveInfinity;
        foreach (var x in v)
        {
            if (double.IsNaN(x)) return double.NaN;
            if (x < m) m = x;
        }
        return m;
    }

    /// <summary>Math.trunc().</summary>
    public static double Trunc(double x) => Math.Truncate(x);

    /// <summary>Converts like JavaScript's `| 0` and array indexing: drops the fraction.</summary>
    public static int I(double x) => (int)x;

    /// <summary>`a || b` for numbers: 0 and NaN count as false.</summary>
    public static double Or(double a, double b) => a == 0 || double.IsNaN(a) ? b : a;

    /// <summary>performance.now(), in milliseconds since the game started.</summary>
    public static double Now() => UnityEngine.Time.realtimeSinceStartupAsDouble * 1000.0;

    /// <summary>Date.now(), in milliseconds since 1970.</summary>
    public static double DateNow() => (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;

    /// <summary>Number.prototype.toFixed().</summary>
    public static string ToFixed(double x, int digits) =>
        (Math.Round(x * Math.Pow(10, digits) + (x >= 0 ? 1e-9 : -1e-9), MidpointRounding.AwayFromZero) / Math.Pow(10, digits))
        .ToString("F" + digits, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>String(number) for the common cases (integers print without a decimal point).</summary>
    public static string Str(double x)
    {
        if (double.IsNaN(x)) return "NaN";
        if (x == Math.Floor(x) && Math.Abs(x) < 1e15) return ((long)x).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return x.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Number.prototype.toLocaleString() for whole numbers: 30000 becomes "30,000".</summary>
    public static string Locale(double x) => ((long)Math.Round(x)).ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("en-US"));

    /// <summary>Fisher-Yates shuffle in place, using Math.random().</summary>
    public static void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = (int)Math.Floor(Random() * (i + 1));
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>Picks a random element, like `arr[Math.floor(Math.random() * arr.length)]`.</summary>
    public static T Pick<T>(IList<T> list) => list[(int)Math.Floor(Random() * list.Count)];

    public static double Clamp(double x, double lo, double hi) => Math.Max(lo, Math.Min(hi, x));
}

/// <summary>Reading Three's userData (a JavaScript object) the way the TypeScript did: `ud.hitRadius ?? 0.42`, `!!ud.coconut`.</summary>
public static class UserDataExt
{
    public static double Num(this Dictionary<string, object> d, string key, double fallback = 0) =>
        d.TryGetValue(key, out var v) && v != null ? Convert.ToDouble(v) : fallback;

    public static bool Flag(this Dictionary<string, object> d, string key) =>
        d.TryGetValue(key, out var v) && v != null && !(v is bool b && !b) && !(v is double x && x == 0) && !(v is int i && i == 0) && !(v is string s && s.Length == 0);

    public static T Get<T>(this Dictionary<string, object> d, string key) where T : class =>
        d.TryGetValue(key, out var v) ? v as T : null;

    public static string Str(this Dictionary<string, object> d, string key) => d.TryGetValue(key, out var v) ? v as string : null;
}
