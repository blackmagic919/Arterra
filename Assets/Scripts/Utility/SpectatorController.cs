using Arterra.Configuration;
using Arterra.GamePlay.Interaction;
using Unity.Mathematics;
using UnityEngine;
[RequireComponent(typeof(Camera))]
public class SpectatorController : MonoBehaviour
{  
    private static SpectatorController Instance {get; set;}
    private static bool HasEnabledSpectator;

    public float moveSpeed = 10f;
    public float fastSpeed = 30f;
    public float lookSensitivity = 2f;

    // Acceleration/deceleration for smooth movement
    public float acceleration = 25f;
    public float deceleration = 40f;
    private float currentSpeed = 0f;

    private float yaw;
    private float pitch;
    private bool active;
    private Camera cam;
    private Camera mainCamera;
    private bool hasSnapped;
    private string origTag;
    private float3 inputDir;
    private bool isSprinting;

    public static void Initialize() {
        Instance = null;
        HasEnabledSpectator = false;
        Config.CURRENT.System.AddHook("Gamemode:Spectator", OnSpectatorRuleChanged);
        object EnableSpectator = Config.CURRENT.GamePlay.Gamemodes.value.SpectatorView;
        OnSpectatorRuleChanged(ref EnableSpectator);
    }

    private static void OnSpectatorRuleChanged(ref object rule) {
        bool EnableSpectator = (bool)rule; 
        if (HasEnabledSpectator == EnableSpectator) return;
        HasEnabledSpectator = EnableSpectator;
        Instance ??= GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Spectator"))
            .GetComponent<SpectatorController>();
        Instance.OnStartup();

        if (!EnableSpectator) {
            InputPoller.RemoveBinding("PMSpectator:TS", "2.0::Subscene");
            Instance.RemoveHandles();
        } else {
            InputPoller.AddBinding(new ActionBind("Toggle Spectator", (_null_) => {
                if (Instance.active) Instance.RemoveHandles();
                else Instance.AddHandles();
            }), "PMSpectator:TS", "2.0::Subscene");   
        }
    }

    public void SetRotation(Quaternion rotation) {
        transform.rotation = rotation;
        Vector3 angles = rotation.eulerAngles;
        yaw = angles.y;
        pitch = angles.x % 360;
        if (pitch > 180) pitch -= 360;
    }

    private void Toggle(Camera cam, bool state) {
# if UNITY_EDITOR
        cam.depth = state ? 0 : -1;
#else 
        cam.enabled = state;
#endif
    }

    void OnStartup() {
        active = false;
        hasSnapped = false;
        origTag = this.tag;
        mainCamera = Camera.main;
        cam = GetComponent<Camera>();
        if (cam == null) Debug.Log("what");
        Toggle(cam, false);
    }

    void Update() {
        // All movement and look handled in PollUpdate if active
        if (!active) return;
        // Mouse look (right mouse button)
        // Only clamp pitch if it has been changed by input, not immediately after snapping
        pitch = Mathf.Clamp(pitch, -89f, 89f);
        transform.eulerAngles = new Vector3(pitch, yaw, 0f);

        // Determine if there is movement input
        Vector3 moveDir = transform.forward * inputDir.y + transform.right * inputDir.x + transform.up * inputDir.z;
        bool hasInput = moveDir.sqrMagnitude > 0.001f;

        float targetSpeed = isSprinting ? fastSpeed : moveSpeed;

        // Accelerate or decelerate currentSpeed
        if (hasInput) {
            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, acceleration * Time.deltaTime);
        } else {
            currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, deceleration * Time.deltaTime);
        }   

        // Only move if currentSpeed > 0
        if (currentSpeed > 0.01f && moveDir.sqrMagnitude > 0.001f) {
            transform.position += moveDir.normalized * currentSpeed * Time.deltaTime;
        }

        // Reset per-frame input
        isSprinting = false;
        inputDir = float3.zero;
    }

    private void AddHandles() {
        if (!hasSnapped && Camera.main != null && Camera.main != this.cam) {
            cam.transform.SetPositionAndRotation(Camera.main.transform.position, Camera.main.transform.rotation);
            SetRotation(cam.transform.rotation);
            hasSnapped = true;
        }
        
        
            InputPoller.AddContextFence("Spectator", "2.5::Subscene", ActionBind.Exclusion.ExcludeAll);
            InputPoller.AddBinding(new ActionBind("Move Vertical", y => inputDir.y = y), "SpectatorMove:MV", "2.5::Subscene");
            InputPoller.AddBinding(new ActionBind("Move Horizontal", x => inputDir.x = x), "SpectatorMove:MH", "2.5::Subscene");
            InputPoller.AddBinding(new ActionBind("Sprint", _ => isSprinting = true), "SpectatorMove:SPR", "2.5::Subscene");
            InputPoller.AddBinding(new ActionBind("Look Horizontal", x => pitch -= x * lookSensitivity), "SpecatatorMove:LH", "2.5::Subscene");
            InputPoller.AddBinding(new ActionBind("Look Vertical", y => yaw += y * lookSensitivity), "SpecatatorMove:LV", "2.5::Subscene"); 
            InputPoller.AddBinding(new ActionBind("Ascend", (_null_) => {
                inputDir.z++;
                }), "SpecatatorMove:ASD", "2.5::Subscene");
                InputPoller.AddBinding(new ActionBind("Descend", (_null_) => {
                inputDir.z--;
                }), "SpecatatorMove:DSD", "2.5::Subscene");
        


        mainCamera = Camera.main;
        // Switch active camera to this one
        if (Camera.main != null && Camera.main != cam) {
            Camera.main.tag = "Untagged";
            Toggle(mainCamera, false);
        }

        Toggle(this.cam, true);
        this.cam.tag = "MainCamera";
        active = true;
    }

    private void RemoveHandles() {
        InputPoller.RemoveContextFence("Spectator", "2.5::Subscene");
        // Switch back to other camera
        if (Camera.main == this.cam) {
            Toggle(this.cam, false);
            this.cam.tag = origTag;
        }

        mainCamera ??= Camera.main;
        Toggle(mainCamera, true);
        mainCamera.tag = "MainCamera";
        active = false;
    }

}
