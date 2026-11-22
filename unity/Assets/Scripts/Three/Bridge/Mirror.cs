using System;

namespace THREE.Bridge
{
    /// <summary>
    /// SPIKE (unity/NATIVE_PLAN.md S1, removed again after it): -spike-mirror draws the whole game the way the native rewrite
    /// would (option A of section 2.1). Every scene hangs under a root scaled (1, 1, -1); local transforms and meshes go to
    /// Unity in Three's numbers, unconverted; cameras, the sun and spot lights stay outside the root and are placed in Unity's
    /// axes. Comparing its screenshots with the normal build's shows whether option A draws the same picture.
    /// </summary>
    internal static class Mirror
    {
        public static readonly bool On = Array.IndexOf(Environment.GetCommandLineArgs(), "-spike-mirror") >= 0;

        /// <summary>The sign z gets on its way to Unity: kept under the mirrored root, flipped by the bridge otherwise.</summary>
        public static readonly float Z = On ? 1f : -1f;
    }
}
