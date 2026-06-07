using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using Arterra.Configuration;
using Arterra.Data.Entity;
using Arterra.Data.Item;
using Arterra.Data.Intrinsic;
using Arterra.Engine.Terrain;
using Arterra.Core.Events;
using Arterra.GamePlay;
using Arterra.GamePlay.Interaction;
using Arterra.GamePlay.UI;
using System;
using Arterra.Data.Entity.Behavior;


namespace Arterra.GamePlay.UI {
    [RequireComponent(typeof(Camera))]
    public class ArmorController : PanelNavbarManager.INavPanel {
        //UI Objects
        private Armor settings;
        private GameObject ArmorPanel;
        private RectTransform ArmorMenu;

        //Scene Objects
        private Camera Camera;
        private GameObject PlayerCamera;
        private FreeCamera CamMovement;
        private Core.ArterraRuntime.IUpdateSubscriber eventTask;
        private ArmorInventory playerArmor;
        private bool IsDragging = false;
        private bool ShowAllSlots = false;
        private int SelectedIndex = -1;
        private IItem Selected => SelectedIndex < 0 ? null : playerArmor.Info[SelectedIndex];
        private static ItemContext GetArmorCxt(ItemContext cxt) => cxt.SetupScenario(PlayerHandler.data, ItemContext.Scenario.ActivePlayerArmor);
        public ArmorController(Armor settings) {
            this.settings = settings;
            PlayerCamera = PlayerHandler.Viewer.Find("SelfCamera").gameObject;
            Camera = PlayerCamera.GetComponent<Camera>();
            CamMovement = new FreeCamera(Camera.transform);

            ArmorPanel = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Armor/ArmorDisplay"), GameUIManager.UIHandle.transform);
            ArmorMenu = ArmorPanel.GetComponent<RectTransform>();
            ArmorPanel.transform.Find("Home").GetComponent<Button>().onClick.AddListener(CamMovement.ClearTransform);
            ArmorPanel.transform.Find("Popups").GetComponent<Button>().onClick.AddListener(() => ShowAllSlots = !ShowAllSlots);
            PlayerCamera.SetActive(false);
            ArmorPanel.SetActive(false);

            settings.Variants.Construct();
            RebindPlayer(null, PlayerHandler.data);
        }

        private bool RebindPlayer(Entity old, Entity cur) {
            var prms = (old, cur);
            return RebindPlayer(ref prms);
        }
        private bool RebindPlayer(ref (Entity old, Entity cur) cxt) {
            if (!cxt.cur.Is(out BehaviorEntity.Animal cInst)) throw new Exception("Respawn expected new Player to be BehaviorEntity");
            if (!cxt.cur.Is(out PlayerInventoriesBehavior cInv)) cInv = null;
            if (cxt.old == null || !cxt.old.Is(out PlayerInventoriesBehavior oInv))
                oInv = null;

            Transform rig = cInst.controller.transform.Find("Player");
            settings.BindBones(rig);

            oInv?.ArmorI.ReleaseDisplay();
            oInv?.ArmorI.UnapplyHandles();
            if (Config.CURRENT.GamePlay.Gamemodes.value.KeepInventory) {
                cInv.ArmorI = oInv?.ArmorI ?? cInv.ArmorI;
            }
            playerArmor = cInv.ArmorI;
            playerArmor.AddCallbacks(GetArmorCxt, GetArmorCxt);
            playerArmor.InitializeDisplay(ArmorPanel);
            playerArmor.ReapplyHandles();

            cxt.cur.eventCtrl.AddEventHandler(
                GameEvent.Entity_Damaged,
                delegate (object actor, object target, object args) {
                    playerArmor.OnDamaged(target as Entity, (RefTuple<(float, float3)>)args);
                }
            );

            cxt.cur.eventCtrl.AddEventHandler(
                GameEvent.Entity_Respawn,
                delegate (object actor, object target, object ctx) {
                    var args = (ctx as RefTuple<(Entity, Entity)>).Value;
                    RebindPlayer(ref args);
                }
            );
            return false;
        }

    public void Activate() {
        this.IsDragging = false;
        this.ShowAllSlots = false;
        this.SelectedIndex = -1;
        PlayerCamera.SetActive(true);
        ArmorPanel.SetActive(true);
        eventTask = new Core.ArterraRuntime.IndirectUpdate(Update);
        PlayerHandler.data.eventCtrl.RaiseEvent(GameEvent.Action_OpenArmor, PlayerHandler.data, null);
        Core.ArterraRuntime.MainLoopUpdateTasks.Enqueue(eventTask);

            
                InputPoller.AddContextFence("PlayerArmor", "3.5::Window", ActionBind.Exclusion.None);
                InputPoller.AddBinding(new ActionBind("Select",
                    _ => Select(), ActionBind.Exclusion.None), "PlayerArmor:SEL", "3.5::Window");
                InputPoller.AddBinding(new ActionBind("Drag Subscene Perspective",
                    _ => RotateCamera(), ActionBind.Exclusion.None), "PlayerArmor:DSL", "3.5::Window");
                InputPoller.AddBinding(new ActionBind("Look Horizontal", CamMovement.LookX), "PlayerArmor:LH", "3.5::Window");
                InputPoller.AddBinding(new ActionBind("Look Vertical", CamMovement.LookY), "PlayerArmor:LV", "3.5::Window");
            

            for (int i = 0; i < playerArmor.Display.Length; i++) {
                float3 originWS = settings.Variants.Retrieve(i).Root.Bone.position;
                playerArmor.Display[i].SetPosition(PositionWSToSS(originWS));
            }
        }

        public void Deactivate() {
            eventTask.Active = false;
            PlayerCamera.SetActive(false);
            ArmorPanel.SetActive(false);
            InputPoller.RemoveContextFence("PlayerArmor", "3.5::Window");
            SetSelectedSlot(-1);
        }

        public Sprite GetNavIcon() => Config.CURRENT.Generation.Textures.Retrieve(settings.DisplayIcon).self;
        public GameObject GetDispContent() => ArmorPanel;

        public void Release() { }

        // Update is called once per frame
        void Update(MonoBehaviour mono) {
            //Look at mouse position
            float2 mousePos = GetPositionVS(((float3)Input.mousePosition).xy);
            float2 headVS = ((float3)Camera.WorldToViewportPoint(settings.HeadBone.position)).xy;
            float2 delta = mousePos - headVS;
            float2 maxDist;
            maxDist.x = delta.x >= 0 ? 1 - headVS.x : headVS.x;
            maxDist.y = delta.y >= 0 ? 1 - headVS.y : headVS.y;

            // normalized look direction in [-1,1]
            float2 lookDir = delta / maxDist;
            lookDir = math.clamp(lookDir, -1f, 1f);

            RefTuple<(float, float)> cxt = (lookDir.y, -lookDir.x);
            PlayerHandler.data.eventCtrl.RaiseEvent(GameEvent.Action_LookDirect, PlayerHandler.data, null, cxt);

            ArmorInventory.ArmorSlot[] ArmorSlots = playerArmor.Display;
            int selected = -1;
            float minDist = float.MaxValue;
            for (int i = 0; i < ArmorSlots.Length; i++) {
                EquipableArmor variant = this.settings.Variants.Retrieve(i);
                float dist = GetRegionDist(variant, mousePos);

                if (dist <= settings.ShrinkDistance) {
                    ArmorSlots[i].active = true;
                    ArmorSlots[i].SetScale(1 - dist / settings.ShrinkDistance);
                    if (dist < minDist) {
                        minDist = dist;
                        selected = i;
                    }
                } else ArmorSlots[i].active = false;

                if (ShowAllSlots) {
                    ArmorSlots[i].active = true;
                    ArmorSlots[i].SetScale(1);
                }

                float3 originVS = Camera.WorldToViewportPoint(variant.Root.Bone.position);
                float2 slotVS = GetPositionVS(ArmorSlots[i].position.xy);
                float distance = math.distance(originVS.xy, slotVS);
                float accel = (variant.TargetSlotDistance - distance) / variant.TargetSlotDistance;
                float2 dir = (math.lengthsq(slotVS - originVS.xy) > 1E-5f
                    ? math.normalize(slotVS - originVS.xy)
                    : UnityEngine.Random.insideUnitCircle.normalized);
                dir *= accel; //attract to target position

                dir += CalculateAvoidanceContribution(slotVS, i);
                ArmorSlots[i].Update(PositionVSToSS(originVS.xy), dir * GetSizeSS(), GetMenuCorners());
            }

            SetSelectedSlot(selected);
            float GetRegionDist(EquipableArmor variant, float2 mousePos) {
                float minDist = float.MaxValue;
                for (int i = 0; i < variant.Bones.value.Count; i++) {
                    EquipableArmor.BoneRegion reg = variant.Bones.value[i];
                    float3 edgeWS = reg.Bone.position + (float3)Camera.main.transform.right * reg.SelectRadius;
                    float3 edgeVS = (float3)Camera.WorldToViewportPoint(edgeWS);
                    float dist = math.distance(edgeVS.xy, mousePos);
                    dist = math.max(dist - reg.SelectRadius, 0);
                    minDist = math.min(minDist, dist);
                }
                return minDist;
            }

            float2 CalculateAvoidanceContribution(float2 slotVS, int self) {
                float2 dir = float2.zero;
                for (int j = 0; j < ArmorSlots.Length; j++) {
                    if (self == j) continue;
                    if (!ArmorSlots[j].active) continue;

                    float2 neighborVS = GetPositionVS(ArmorSlots[j].position.xy);
                    float sepDist = math.distance(neighborVS, slotVS);
                    if (sepDist >= settings.SeperationDistance) continue;
                    float sepAccel = (settings.SeperationDistance - sepDist) / settings.SeperationDistance;
                    float2 sepDir = (math.lengthsq(slotVS - neighborVS) > 1E-5f
                        ? math.normalize(slotVS - neighborVS)
                        : UnityEngine.Random.insideUnitCircle.normalized);
                    dir += sepAccel * sepDir;
                }
                return dir;
            }
        }

        private static InventoryController.CursorManager Cursor => InventoryController.Cursor;

        private void Select() {
            if (!IsMouseInMenu()) return;
            if (IsDragging) IsDragging = false;
            else if (SelectedIndex == -1) {
                IsDragging = true;
                return;
            }

            if (Cursor.IsSplitUp) return;
            InputPoller.SuspendKeybindPropogation("Select", ActionBind.Exclusion.ExcludeLayerByName);

            if (Cursor.IsHolding) {
                if (Cursor.Item is not ArmorInventory.IArmorItem armor
                    || !playerArmor.AddEntry(armor, SelectedIndex)
                ) {
                    InventoryController.DropItem(Cursor.Item);
                }
                Cursor.ClearCursor();
            } else {
                if (Selected == null) return;
                IItem heldItem = Selected.Clone() as IItem;
                playerArmor.RemoveEntry(SelectedIndex);
                Cursor.HoldItem(heldItem);
            }
        }
        private void RotateCamera() {
            if (!IsDragging) return;
            CamMovement.Rotate();
        }

        private bool IsMouseInMenu() {
            float2 mouseSS = ((float3)Input.mousePosition).xy;
            float2 bottomLeft; float2 topRight;
            (bottomLeft, topRight) = GetMenuCorners();
            return math.all(mouseSS >= bottomLeft) && math.all(mouseSS <= topRight);
        }

        public void SetSelectedSlot(int index) {
            if (index == SelectedIndex) return;
            if (SelectedIndex != -1)
                playerArmor.Display[SelectedIndex].SetSelect(false);
            SelectedIndex = index;
            if (index == -1) return;
            playerArmor.Display[index].SetSelect(true);
        }
        private (float2, float2) GetMenuCorners() {
            Vector3[] corners = new Vector3[4];
            ArmorMenu.GetWorldCorners(corners);
            float2 bottomLeft = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
            float2 topRight = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
            return (bottomLeft, topRight);
        }

        private float2 GetPositionVS(float2 positionSS) {
            float2 bottomLeft; float2 topRight;
            (bottomLeft, topRight) = GetMenuCorners();

            positionSS.x = Mathf.InverseLerp(bottomLeft.x, topRight.x, positionSS.x);
            positionSS.y = Mathf.InverseLerp(bottomLeft.y, topRight.y, positionSS.y);
            return positionSS;
        }

        private float2 PositionVSToSS(float2 positionVS) {
            float2 bottomLeft; float2 topRight;
            (bottomLeft, topRight) = GetMenuCorners();

            positionVS.x = Mathf.Lerp(bottomLeft.x, topRight.x, positionVS.x);
            positionVS.y = Mathf.Lerp(bottomLeft.y, topRight.y, positionVS.y);
            return positionVS;
        }

        private float2 PositionWSToSS(float3 positionWS) {
            float3 posVS = (float3)Camera.WorldToViewportPoint(positionWS);
            return PositionVSToSS(posVS.xy);
        }

        private float2 GetSizeSS() {
            float2 bottomLeft; float2 topRight;
            (bottomLeft, topRight) = GetMenuCorners();
            return topRight - bottomLeft;
        }

        private class FreeCamera {
            private PlayerCameraSettings S;
            private Transform CamTsf;
            const float height = 0f;
            const float distance = 7.5f;
            private float yaw;
            private float pitch;
            private float2 Rot;

            public FreeCamera(Transform camera) {
                if (!Config.CURRENT.GamePlay.PlayerSettings.value.value.Is(out S))
                    throw new Exception("Free Camera expected to find camera settings on player");
                this.CamTsf = camera;
                ClearTransform();
            }

            public void ClearTransform() {
                pitch = 0; yaw = 180;
                CamTsf.localRotation = Quaternion.AngleAxis(yaw, Vector3.up);
                SetCameraOffset();
            }

            public void LookX(float x) => Rot.x = x * S.Sensitivity;
            public void LookY(float y) => Rot.y = y * S.Sensitivity;

            public void Rotate() {
                yaw += Rot.y;
                pitch -= Rot.x;
                if (S.clampVerticalRotation)
                    pitch = Mathf.Clamp(pitch, S.MinimumX, S.MaximumX);

                Quaternion targRot = Quaternion.AngleAxis(yaw, Vector3.up) *
                                    Quaternion.AngleAxis(pitch, Vector3.right);
                if (S.smooth) {
                    CamTsf.localRotation = Quaternion.Slerp(CamTsf.localRotation, targRot,
                        S.smoothTime * Time.deltaTime);
                } else CamTsf.localRotation = targRot;
                SetCameraOffset();
            }

            private void SetCameraOffset() {
                float3 backOffset = math.mul(math.normalize(CamTsf.localRotation), new float3(0, 0, -distance));
                CamTsf.localPosition = GetScaledLocalPosition((float3)Vector3.up * height + backOffset);
            }

            private Vector3 GetScaledLocalPosition(float3 localOffset) {
                if (CamTsf.parent == null)
                    return localOffset;

                Vector3 desiredWorldOffset = CamTsf.parent.rotation * (Vector3)localOffset;
                return CamTsf.parent.worldToLocalMatrix.MultiplyVector(desiredWorldOffset);
            }
        }
    }
}