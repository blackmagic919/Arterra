using Unity.Mathematics;
using UnityEngine;
using Arterra.Configuration;
using Arterra.Core.Terrain;
using Arterra.Core.Player;

public static class PlayerCrosshair {
    private static GameObject Crosshair;
    private static Animator Animator;
    private static IndirectUpdate executor;
    private static bool enabled;
    private static bool active;
    public static void Initialize() {
        Crosshair = Resources.Load<GameObject>("Prefabs/GameUI/Crosshair/Crosshair");
        Crosshair = Object.Instantiate(Crosshair, GameUIManager.UIHandle.transform, worldPositionStays:false);
        Animator = Crosshair.GetComponent<Animator>();
        Crosshair.SetActive(false);
        
        Config.CURRENT.System.GameplayModifyHooks.Add("ToggleUICrosshair", ToggleCrosshair);
        object show = Config.CURRENT.GamePlay.Player.value.ShowCrosshair;
        ToggleCrosshair(ref show);
    }

    private static void ToggleCrosshair(ref object nState) {
        enabled = (bool)nState;
        bool show = enabled && active;
        ToggleCrosshair(show);
    }
    
    public static void EnableCrosshair() {
        active = true;
        bool show = enabled && active;
        ToggleCrosshair(show);
    }

    public static void DisableCrosshair() {
        active = false;
        bool show = enabled && active;
        ToggleCrosshair(show);
    }

    private static void ToggleCrosshair(bool show) {
        if (Crosshair == null) return;
        if (Crosshair.activeSelf == show) return;
        Crosshair.SetActive(show);

        if (executor != null) {
            executor.Active = false;
            executor = null;
        }

        if (Crosshair.activeSelf) {
            executor = new IndirectUpdate(Update);
            OctreeTerrain.MainLoopUpdateTasks.Enqueue(executor);
        }
    }

    

    public static void Update(MonoBehaviour mono) {
        PlayerStreamer.Player player = PlayerHandler.data;
        float3 hitPt = player.head
                + player.Forward
                * Config.CURRENT.GamePlay.Player.value.Interaction.value.ReachDistance;

        if (PlayerInteraction.RayTestSolid(out float3 terrHit)) hitPt = terrHit;
        if (!EntityManager.ESTree.FindClosestAlongRay(player.head, hitPt, player.info.entityId, out _))
            Animator.SetBool("Focus", false);
        else Animator.SetBool("Focus", true);
    }
}