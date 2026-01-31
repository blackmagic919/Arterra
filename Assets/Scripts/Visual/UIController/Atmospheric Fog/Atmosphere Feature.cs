using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Arterra.Engine.Rendering
{
    public class AtmosphereFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class PassSettings
        {
            public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        }


        // References to our pass and its settings.
        public AtmospherePass pass;
        public PassSettings passSettings = new();

        public override void Create()
        {
            pass = new AtmospherePass(passSettings);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // Here you can queue up multiple passes after each other.
            renderer.EnqueuePass(pass);
        }
    }
}