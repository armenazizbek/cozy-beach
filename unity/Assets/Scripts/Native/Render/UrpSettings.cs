using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace THREE.Render
{
    /// <summary>The few things that talk to URP directly: render scale, shadows, camera setup and drawing into a texture.</summary>
    internal static class UrpSettings
    {
        public static bool ShadowsOn = true;

        private static UniversalRenderPipelineAsset Asset => GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

        public static void SetRenderScale(double r)
        {
            var a = Asset;
            if (a == null) return;
            float s = Mathf.Clamp((float)r, 0.25f, 2f);
            if (!Mathf.Approximately(a.renderScale, s)) a.renderScale = s;
        }

        public static void SetShadows(bool on)
        {
            if (ShadowsOn == on) return;
            ShadowsOn = on;
            var a = Asset;
            if (a != null) a.shadowDistance = on ? ShadowDistance : 0f;
        }

        /// <summary>How far shadows reach; set from the sun's shadow camera.</summary>
        public static float ShadowDistance = 60f;

        public static void SetShadowDistance(float d)
        {
            ShadowDistance = d;
            var a = Asset;
            if (a != null && ShadowsOn && !Mathf.Approximately(a.shadowDistance, d)) a.shadowDistance = d;
        }

        /// <summary>Soft (filtered) or hard shadow edges for the game's lights (Lights.Shadows).</summary>
        public static bool SoftShadows = true;

        /// <summary>
        /// The graphics quality the player picked: "low", "medium" or "high" (high is how the game always looked). Low and
        /// medium trade antialiasing, shadow detail and shadow softness for speed on slower graphics chips.
        /// </summary>
        /// <summary>things whose bounding box diagonal is smaller than this (metres) cast no shadow; 0 = all cast (medium, high)</summary>
        public static float SmallCasterCutoff;

        /// <summary>bumped when the cutoff changes, so every object looks again</summary>
        public static int CasterVersion;

        public static void SetQuality(string q)
        {
            var a = Asset;
            if (a == null) return;
            int msaa, shadowRes, cascades;
            float reach = 60f;
            var softQuality = SoftShadowQuality.Medium;
            switch (q)
            {
                case "low": msaa = 1; shadowRes = 1024; cascades = 1; SoftShadows = false; reach = 40f; break;
                // the Steam Deck: half the antialiasing, one smaller shadow map over a shorter reach, cheaper soft edges
                case "handheld": msaa = 2; shadowRes = 2048; cascades = 1; SoftShadows = true; softQuality = SoftShadowQuality.Low; reach = 35f; break;
                case "medium": msaa = 2; shadowRes = 2048; cascades = 2; SoftShadows = true; break;
                default: msaa = 4; shadowRes = 4096; cascades = 2; SoftShadows = true; break;
            }
            float cutoff = q == "low" || q == "handheld" ? 0.5f : 0f;
            if (cutoff != SmallCasterCutoff)
            {
                SmallCasterCutoff = cutoff;
                CasterVersion++;
            }
            SetSoftShadowQuality(a, softQuality);
            SetShadowDistance(reach);
            if (a.msaaSampleCount != msaa) a.msaaSampleCount = msaa;
            if (a.mainLightShadowmapResolution != shadowRes) a.mainLightShadowmapResolution = shadowRes;
            if (a.shadowCascadeCount != cascades) a.shadowCascadeCount = cascades;
        }

        private static System.Reflection.FieldInfo softField;

        /// <summary>The asset has no public setter for how soft the shadow edges are, only its serialized field.</summary>
        private static void SetSoftShadowQuality(UniversalRenderPipelineAsset a, SoftShadowQuality q)
        {
            try
            {
                softField ??= typeof(UniversalRenderPipelineAsset).GetField("m_SoftShadowQuality", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                softField?.SetValue(a, q);
            }
            catch
            {
                /* keep the asset's own */
            }
        }

        /// <summary>How soft the shadow edges are now, for the smoke test's log.</summary>
        public static string SoftShadowQualityName()
        {
            try
            {
                softField ??= typeof(UniversalRenderPipelineAsset).GetField("m_SoftShadowQuality", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                return softField?.GetValue(Asset)?.ToString() ?? "?";
            }
            catch
            {
                return "?";
            }
        }

        public static void SetupCamera(UnityEngine.Camera cam)
        {
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null) return;
            data.renderShadows = true;
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.requiresDepthOption = CameraOverrideOption.Off;
            data.requiresColorOption = CameraOverrideOption.Off;
        }

        public static void RenderTo(UnityEngine.Camera cam, RenderTexture rt)
        {
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else
            {
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = null;
            }
        }
    }
}
