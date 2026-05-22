using UnityEngine;
using Arterra.GamePlay;
using DevEffects;
using Arterra.Core.Storage;
using Arterra.Data.Entity.Behavior;

public class FollowRay : MonoBehaviour
{
    public Material rayMaterial;
    public float rayDrawDuration = 1.0f;
    public float rayWidth = 0.1f;
    private Camera spectatorCam;
    private LineRenderer lineRenderer;
    private SpectatorController specCtrl;
    private bool following = false;
    private float drawTimer = 0f;
    private Vector3 rayStart, rayEnd;
    private Vector3 offset;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.P))
        {
            TrySwitchToSpectatorAndStartRay();
        }
        if (following)
        {
            AnimateRay();
        }
    }

    void TrySwitchToSpectatorAndStartRay()
    {
        // Find the spectator camera
        specCtrl = FindObjectOfType<SpectatorController>();
        if (specCtrl == null) return;
        spectatorCam = specCtrl.GetComponent<Camera>();
        offset = CPUMapManager.WSToGS(specCtrl.transform.position) - PlayerHandler.data.head;
        if (spectatorCam == null) return;
        spectatorCam.transform.LookAt(PlayerHandler.data.head);
        specCtrl.SetRotation(spectatorCam.transform.rotation);

        // Switch to spectator camera
        Camera[] allCams = Camera.allCameras;
        foreach (var cam in allCams)
        {
            cam.enabled = (cam == spectatorCam);
        }
        spectatorCam.tag = "MainCamera";

        // Get ray start and end
        if (PlayerHandler.data == null) return;
        rayStart = PlayerHandler.data.head;
        if (PlayerHandler.data.Is(out PlayerInteractionBehavior interact)) return;
        if (!interact.RayTestSolid(out Unity.Mathematics.float3 hit))
            rayEnd = rayStart + (Vector3)PlayerHandler.data.Forward * 20f;
        else
            rayEnd = new Vector3(hit.x, hit.y, hit.z);

        // Setup line renderer
        if (lineRenderer == null)
        {
            var go = new GameObject("FollowRayLine");
            lineRenderer = go.AddComponent<LineRenderer>();
            lineRenderer.material = rayMaterial != null ? rayMaterial : new Material(Shader.Find("Sprites/Default"));
            lineRenderer.startColor = Color.yellow;
            lineRenderer.endColor = Color.yellow;
            lineRenderer.startWidth = rayWidth;
            lineRenderer.endWidth = rayWidth;
            lineRenderer.positionCount = 2;
        }
        lineRenderer.gameObject.SetActive(true);
        lineRenderer.SetPosition(0, rayStart);
        lineRenderer.SetPosition(1, rayStart);
        drawTimer = 0f;

        following = true;
    }

    void AnimateRay()
    {
        drawTimer += Time.deltaTime;
        float t = Mathf.Clamp01(drawTimer / rayDrawDuration);
        Vector3 currentEnd = Vector3.Lerp(rayStart, rayEnd, t);
        lineRenderer.SetPosition(0, CPUMapManager.GSToWS(rayStart));
        lineRenderer.SetPosition(1, CPUMapManager.GSToWS(currentEnd));
        if (spectatorCam != null)
        {
            // Camera follows the midpoint of the ray as it's drawn
            spectatorCam.transform.position = CPUMapManager.GSToWS(Vector3.Lerp(rayStart, rayEnd, Mathf.Clamp01(drawTimer / rayDrawDuration)) + offset);
        }

        if (t >= 1f) {
            following = false;
        }
    }
}
