using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace THREE.Bridge
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
