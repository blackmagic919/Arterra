using System.Collections.Generic;
using TerrainGeneration;
using Unity.Burst.Intrinsics;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using WorldConfig;
using WorldConfig.Generation.Entity;
using WorldConfig.Generation.Item;
using WorldConfig.Intrinsic;

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
    private IUpdateSubscriber eventTask;
    private ArmorInventory playerArmor;
    private static uint ContextFence;
    private bool IsDragging = false;
    private bool ShowAllSlots = false;
    private int SelectedIndex = -1;
    private IItem Selected => playerArmor.Info[SelectedIndex];
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
        ContextFence = 0;

        settings.Variants.Construct();
        RebindPlayer(null, PlayerHandler.data);
    }

    private bool RebindPlayer(PlayerStreamer.Player old, PlayerStreamer.Player cur) {
        var prms = (old, cur);
        return RebindPlayer(ref prms);
    }
    private bool RebindPlayer(ref (PlayerStreamer.Player old, PlayerStreamer.Player cur) cxt) {
        Transform rig = cxt.cur.player.transform.Find("Player");
        settings.BindBones(rig);

        cxt.old?.ArmorI.ReleaseDisplay();
        cxt.old?.ArmorI.UnapplyHandles();
        if (Config.CURRENT.GamePlay.Gamemodes.value.KeepInventory) {
            cxt.cur.ArmorI = cxt.old?.ArmorI ?? cxt.cur.ArmorI;
        } playerArmor = cxt.cur.ArmorI;
        playerArmor.AddCallbacks(GetArmorCxt, GetArmorCxt);
        playerArmor.InitializeDisplay(ArmorPanel);
        playerArmor.ReapplyHandles();

        cxt.cur.Events.AddEvent<(float, float3, Entity)>(
            EntityEvents.EventType.OnDamaged,
            playerArmor.OnDamaged
        );

        cxt.cur.Events.AddEvent<(PlayerStreamer.Player, PlayerStreamer.Player)>(
            EntityEvents.EventType.OnRespawn,
            RebindPlayer
        );
        return false;
    }

    public void Activate() {
        this.IsDragging = false;
        this.ShowAllSlots = false;
        this.SelectedIndex = -1;
        PlayerCamera.SetActive(true);
        ArmorPanel.SetActive(true);
        eventTask = new IndirectUpdate(Update);
        OctreeTerrain.MainLoopUpdateTasks.Enqueue(eventTask);

        InputPoller.AddKeyBindChange(() => {
            ContextFence = InputPoller.AddContextFence("3.0::Window", InputPoller.ActionBind.Exclusion.None);
            InputPoller.AddBinding(new InputPoller.ActionBind("Select",
                _ => Select(), InputPoller.ActionBind.Exclusion.None), "3.0::Window");
            InputPoller.AddBinding(new InputPoller.ActionBind("Deselect",
                _ => Deselect(), InputPoller.ActionBind.Exclusion.None), "3.0::Window");
            InputPoller.AddBinding(new InputPoller.ActionBind("Look Horizontal", CamMovement.LookX), "3.0::Window");
            InputPoller.AddBinding(new InputPoller.ActionBind("Look Vertical", CamMovement.LookY), "3.0::Window");
        });

        for (int i = 0; i < playerArmor.Display.Length; i++) {
            float3 originWS = settings.Variants.Retrieve(i).Root.Bone.position;
            playerArmor.Display[i].SetPosition(PositionWSToSS(originWS));
        }
    }

    public void Deactivate() {
        eventTask.Active = false;
        PlayerCamera.SetActive(false);
        ArmorPanel.SetActive(false);
        InputPoller.AddKeyBindChange(() => {
            if (ContextFence == 0) return;
            InputPoller.RemoveContextFence(ContextFence, "3.0::Window");
            ContextFence = 0;
        });
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

        PlayerHandler.data.Play("LookDirect", lookDir.y, -lookDir.x);
        //Rotate camera if dragging
        if (IsDragging) CamMovement.Rotate();
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

    private void Select() {
        if (!IsMouseInMenu()) return;
        if (SelectedIndex == -1) {
            IsDragging = true;
            return;
        };

        InventoryController.InventorySlotDisplay disp = InventoryController.CursorDisplay;
        InventoryController.Cursor = Selected;
        disp.Object.SetActive(true);

        if (InventoryController.Cursor == null) return;
        playerArmor.RemoveEntry(SelectedIndex);
        InventoryController.Cursor.AttachDisplay(disp.Object.transform);
        InputPoller.SuspendKeybindPropogation("Select", InputPoller.ActionBind.Exclusion.ExcludeLayer);
    }
    private void Deselect() {
        IsDragging = false;
        if (!IsMouseInMenu()) return;
        if (SelectedIndex == -1) return;
        if (InventoryController.Cursor == null) return;
        
        InventoryController.InventorySlotDisplay disp = InventoryController.CursorDisplay;
        IItem cursor = InventoryController.Cursor;
        cursor.ClearDisplay(disp.Object.transform);
        InventoryController.Cursor = null;
        disp.Object.SetActive(false);

        if (cursor is not ArmorInventory.IArmorItem armor
          || !playerArmor.AddEntry(armor, SelectedIndex)
        ) {
            InventoryController.DropItem(cursor);
        }
        InputPoller.SuspendKeybindPropogation("Deselect", InputPoller.ActionBind.Exclusion.ExcludeLayer);
        
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
        private WorldConfig.Gameplay.Player.Camera S => Config.CURRENT.GamePlay.Player.value.Camera;
        private Transform CamTsf;
        const float height = 0f;
        const float distance = 7.5f;
        private float yaw;
        private float pitch;
        private float2 Rot;

        public FreeCamera(Transform camera) {
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
            CamTsf.localPosition = (float3)Vector3.up * height + backOffset;
        }
    }
}
