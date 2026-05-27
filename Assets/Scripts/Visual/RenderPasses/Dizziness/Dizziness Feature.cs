using UnityEngine.Rendering.Universal;

namespace Arterra.Engine.Rendering {
    public class DizzinessFeature : ScriptableRendererFeature {
        [System.Serializable]
        public class PassSettings {
            public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        }

        public DizzinessPass pass;
        public PassSettings passSettings = new();

        public override void Create() {
            pass = new DizzinessPass(passSettings);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
            if (!DizzinessPass.IsActive()) return;
            renderer.EnqueuePass(pass);
        }
    }
}
