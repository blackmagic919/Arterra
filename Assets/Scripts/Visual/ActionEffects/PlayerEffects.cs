using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using Arterra.Core.Terrain;
using Arterra.Core.Player;

//This is the visual layer, in the future this information would need to be
//sent over to the server so other clients can see your animations
public interface IActionEffect {
    public void Play(string action, params object[] args);
    public delegate void ActionEffect(params object[] args);
}

public class PlayerActionEffects : IActionEffect{
    private bool IsShaking = false;
    private GameObject player;
    private Animator animator;
    private GameObject HeldItem;
    private Dictionary<string, IActionEffect.ActionEffect> Actions;
    public void Initialize(GameObject player) {
        this.player = player;
        this.animator = player.transform.Find("Player").GetComponent<Animator>();
        Actions = new Dictionary<string, IActionEffect.ActionEffect>() {
            {"Consume", PlayConsume},
            {"RemoveTerrain", PlayPlace},
            {"PlaceTerrain", PlayPlace},
            {"LookGradual", PlayLookGradual },
            {"LookDirect", PlayLookDirect },
            {"Die", PlayDead },
            {"RecieveDamage", PlayDamaged},
            {"Jump", PlayJump},
            {"Land", PlayLand},
            {"SitDown", PlaySit},
            {"StandUp", PlayStand},
            {"StartSwim", PlayStartSwim},
            {"StopSwim", PlayStopSwim},
            {"PlayMove", PlayMove},
            {"Punch", PlayPunch},
            {"HoldItem", PlayHoldItem},
            {"UnHoldItem", PlayUnHoldItem},
            {"Thrust", PlayThrust},
            {"Swing", PlaySwing},
            {"Slash", PlaySlash},
            {"DrawBow", PlayDrawBow},
            {"ReleaseBow", PlayReleaseBow}
        };
        
        IsShaking = false;
    }
    public void Play(string action, params object[] args) {
        if (!Actions.TryGetValue(action, out IActionEffect.ActionEffect effect))
            return;
        effect.Invoke(args);
    }

    private void PlayConsume(params object[] args) => animator.SetTrigger("Eat");
    public void PlayPlace(params object[] args) => animator.SetTrigger("Place");
    private void PlaySlash(params object[] args) => animator.SetTrigger("Slash");
    private void PlayThrust(params object[] args) => animator.SetTrigger("Thrust");
    private void PlaySwing(params object[] args) => animator.SetTrigger("Swing");
    private void PlayStopSwim(params object[] args) => animator.SetBool("IsSwimming", false);
    private void PlayStand(params object[] args) => animator.SetBool("IsSitting", false);
    private void PlayDead(params object[] args) => animator.SetBool("IsDead", true);
    private void PlayJump(params object[] args) => animator.SetBool("IsJumping", true);
    private void PlayDrawBow(params object[] args) {
        animator.SetBool("IsDrawingBow", true);
        if (HeldItem?.TryGetComponent<Animator>(out Animator bowAnim) == null)
            return;
        bowAnim.SetBool("IsDrawingBow", true);
    }
    private void PlayReleaseBow(params object[] args) {
        animator.SetBool("IsDrawingBow", false);
        if (HeldItem == null || !HeldItem.TryGetComponent(out Animator bowAnim))
            return;
        bowAnim.SetBool("IsDrawingBow", false);
    }
    private void PlayMove(params object[] args) {
        float3 normVel = (float3)args[0];
        float speed = Mathf.Lerp(animator.GetFloat("Speed"), (float)math.length(normVel), 0.35f);
        float angleTheta = Mathf.Atan2(normVel.z, normVel.x) / Mathf.PI;
        angleTheta = Mathf.Lerp(animator.GetFloat("MoveTheta"), angleTheta, 0.35f);
        
        animator.SetFloat("Speed", speed);
        animator.SetFloat("MoveTheta", angleTheta);
    }
    private void PlayStartSwim(params object[] args) {
        animator.SetBool("IsSwimming", true);
        animator.SetBool("IsJumping", false);
        animator.SetBool("IsSitting", false);
    }
    private void PlaySit(params object[] args) {
        animator.SetBool("IsSitting", true);
        animator.SetBool("IsJumping", false);
        animator.SetBool("IsSwimming", false);
    }
    private void PlayPunch(params object[] args) {
        string anim = UnityEngine.Random.Range(0, 2) == 0 ? "PunchR" : "PunchL";
        animator.SetTrigger(anim);
    }
    private void PlayLand(params object[] args) {
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (!state.IsName("hRig_fall")) return;
        animator.SetBool("IsJumping", false);
    }

    private void PlayLookGradual(params object[] args) {
        float pitch = (float)args[0];
        float deltaYaw = (float)args[1];
        animator.SetBool("DisableShuffle", false);
        animator.SetFloat("LookY", pitch);
        float yaw = animator.GetFloat("LookX");
        yaw = (yaw + deltaYaw) * 0.75f;
        animator.SetFloat("LookX", yaw);
    }

    private void PlayLookDirect(params object[] args) {
        float pitch = (float)args[0];
        float yaw = (float)args[1];
        animator.SetBool("DisableShuffle", true);
        animator.SetFloat("LookY", pitch);
        animator.SetFloat("LookX", yaw);
    }

    private void PlayDamaged(params object[] args) {
        OctreeTerrain.MainCoroutines.Enqueue(CameraShake(0.2f, 0.25f));

        float3 KnockbackDir = (float3)args[1];
        quaternion WSToOS = math.inverse(PlayerHandler.data.transform.rotation);
        KnockbackDir = math.mul(WSToOS, KnockbackDir);
        if (math.lengthsq(KnockbackDir) < 1E-6) return;
        KnockbackDir = math.normalize(KnockbackDir);
        animator.SetFloat("KnockZ", KnockbackDir.z);
        float xStr = 1 - math.abs(KnockbackDir.z);
        animator.SetFloat("KnockX", KnockbackDir.x * xStr);
        float yStr = (1 - math.abs(KnockbackDir.z)) * xStr;
        animator.SetFloat("KnockY", KnockbackDir.y * yStr);
    }

    private void PlayHoldItem(params object[] args) {
        GameObject item = (GameObject)args[0];
        Transform handle = player.transform.Find("Player/hRig/spine/spine.001/spine.002/spine.003/shoulder.L/upper_arm.L/forearm.L/hand.L/handle");
        HeldItem = GameObject.Instantiate(item, handle, false);
    }

    private void PlayUnHoldItem(params object[] args) {
        if (HeldItem == null) return;
        GameObject.Destroy(HeldItem);
        HeldItem = null;
    }
    
    private IEnumerator CameraShake(float duration, float rStrength)
    {
        if (IsShaking) yield break; //Stop
        IsShaking = true;

        Transform CameraLocalT = PlayerHandler.Camera.GetChild(0);
        Quaternion OriginRot = CameraLocalT.localRotation;
        Quaternion RandomRot = Quaternion.Euler(new(0, 0, UnityEngine.Random.Range(-180f, 180f) * rStrength));

        float elapsed = 0.0f;
        while (elapsed < duration)
        {
            CameraLocalT.localRotation = Quaternion.Slerp(CameraLocalT.localRotation, RandomRot, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        elapsed = 0.0f;
        while (elapsed < duration)
        {
            CameraLocalT.localRotation = Quaternion.Slerp(CameraLocalT.localRotation, OriginRot, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        
        CameraLocalT.localRotation = OriginRot;
        IsShaking = false;
    }
}