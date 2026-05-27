using UnityEngine.Rendering.Universal;

namespace Arterra.Engine.Rendering {
    public class BlindnessFeature : ScriptableRendererFeature {
        [System.Serializable]
        public class PassSettings {
            public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        }

        public BlindnessPass pass;
        public PassSettings passSettings = new();

        public override void Create() {
            pass = new BlindnessPass(passSettings);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
            if (!BlindnessPass.IsActive()) return;
            renderer.EnqueuePass(pass);
        }
    }
}
