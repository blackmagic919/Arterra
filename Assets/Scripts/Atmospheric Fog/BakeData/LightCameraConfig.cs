using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteInEditMode]
public class LightCameraConfig : MonoBehaviour
{
    RenderTargetIdentifier shadowmap;
    RenderTexture m_ShadowmapCopy;//
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        CommandBuffer cmd = new CommandBuffer();
        shadowmap = BuiltinRenderTextureType.CurrentActive;
        m_ShadowmapCopy = new RenderTexture(1024, 1024, 16, RenderTextureFormat.ARGB32);
        m_ShadowmapCopy.filterMode = FilterMode.Point;//

        cmd.SetShadowSamplingMode(shadowmap, ShadowSamplingMode.RawDepth);
        var id = new RenderTargetIdentifier(m_ShadowmapCopy);
        cmd.Blit(shadowmap, id);
        cmd.SetGlobalTexture("m_ShadowRawDepth", id);
        Light m_Light = this.GetComponent<Light>();
        m_Light.AddCommandBuffer(LightEvent.AfterShadowMap, cmd);
    }
}
