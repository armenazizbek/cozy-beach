using System;

namespace THREE.Bridge
{
    /// <summary>
    /// How the bridge hands Three's right-handed numbers to Unity (unity/NATIVE_PLAN.md 2.1, option A, proven in S1). Every
    /// scene hangs under a root scaled (1, 1, -1) (World.Root for the main one); local transforms and meshes go to Unity in
    /// Three's numbers, unconverted; cameras, the sun and spot lights stay outside the root and are placed in Unity's axes.
    /// Native objects use the same convention, so they can hang under bridge objects directly. -bridge-legacy brings back the
    /// old per-object conversion (every transform and mesh mirrored) for comparison; it goes with the bridge in S25.
    /// </summary>
    internal static class Mirror
    {
        public static readonly bool On = Array.IndexOf(Environment.GetCommandLineArgs(), "-bridge-legacy") < 0;

        /// <summary>The sign z gets on its way to Unity: kept under the mirrored root, flipped by the bridge otherwise.</summary>
        public static readonly float Z = On ? 1f : -1f;
    }
}
