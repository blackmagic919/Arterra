using Unity.Mathematics;
using UnityEngine;
using Arterra.Configuration;
using Arterra.Engine.Terrain;
using Arterra.GamePlay;
using Arterra.Data.Entity.Behavior;

namespace Arterra.GamePlay.UI {
    public static class PlayerCrosshair {
        private static GameObject Crosshair;
        private static Animator Animator;
        private static IndirectUpdate executor;
        private static bool enabled;
        private static bool active;
        public static void Initialize() {
            Crosshair = Resources.Load<GameObject>("Prefabs/GameUI/Crosshair/Crosshair");
            Crosshair = Object.Instantiate(Crosshair, GameUIManager.UIHandle.transform, worldPositionStays: false);
            Animator = Crosshair.GetComponent<Animator>();
            Crosshair.SetActive(false);

            Config.CURRENT.System.AddHook("ToggleUICrosshair", ToggleCrosshair);
            object show = Config.CURRENT.GamePlay.Gamemodes.value.ShowCrosshair;
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
            BehaviorEntity.Animal player = PlayerHandler.data;
            float3 hitPt = player.head
                    + player.Forward
                    * PlayerInteractionSettings.GetSingleton().ReachDistance;

            if (!player.Is(out PlayerInteractionBehavior interact)) return;
            if (interact.RayTestSolid(out float3 terrHit)) hitPt = terrHit;
            if (!EntityManager.ESTree.FindClosestAlongRay(player.head, hitPt, player.info.rtEntityId, out _, out _))
                Animator.SetBool("Focus", false);
            else Animator.SetBool("Focus", true);
        }
    }
}