using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using Arterra.Core.Terrain;
using Arterra.Core.Player;
using Arterra.Configuration;
using Arterra.Core.Events;
using Arterra.Configuration.Generation.Entity;
using Arterra.Core.Storage;

public class PlayerActionEffects{
    private bool IsShaking = false;
    private bool IsSwimming = false;
    private GameObject player;
    private Animator animator;
    private GameObject HeldItem;
    public void Initialize(GameObject player, EventControl evntCntrl) {
        this.player = player;
        this.animator = player.transform.Find("Player").GetComponent<Animator>();
        evntCntrl.AddEventHandler<float3>(GameEvent.Action_Jump, PlayJump);
        evntCntrl.AddEventHandler<float>(GameEvent.Item_ConsumeFood, PlayConsume);
        evntCntrl.AddEventHandler<float3>(GameEvent.Action_RemoveTerrain, PlayPlace);
        evntCntrl.AddEventHandler<float3>(GameEvent.Action_PlaceTerrain, PlayPlace);
        evntCntrl.AddEventHandler<(float, float)>(GameEvent.Action_LookGradual, PlayLookGradual);
        evntCntrl.AddEventHandler<(float, float)>(GameEvent.Action_LookDirect, PlayLookDirect);
        evntCntrl.AddEventHandler<object>(GameEvent.System_Deserialize, DeserializeAnimator);
        evntCntrl.AddEventHandler<object>(GameEvent.System_Deserialize, DeserializeAnimator);
        evntCntrl.AddEventHandler<object>(GameEvent.Entity_Death, PlayDead);
        evntCntrl.AddEventHandler<(float, float3)>(GameEvent.Entity_Damaged, PlayDamaged);
        evntCntrl.AddEventHandler<float>(GameEvent.Entity_HitGround, PlayTouchdown);
        evntCntrl.AddEventHandler<object>(GameEvent.Action_MountRideable, PlaySit);
        evntCntrl.AddEventHandler<object>(GameEvent.Action_DismountRideable, PlayStand);
        evntCntrl.AddEventHandler<float>(GameEvent.Entity_InLiquid, PlayStartSwim);
        evntCntrl.AddEventHandler<float>(GameEvent.Entity_InGas, PlayStopSwim);
        evntCntrl.AddEventHandler<(float, float3)>(GameEvent.Entity_Attack, PlayPunch);
        evntCntrl.AddEventHandler<GameObject>(GameEvent.Item_HoldTool, PlayHoldItem);
        evntCntrl.AddEventHandler<GameObject>(GameEvent.Item_UnholdTool, PlayUnHoldItem);
        evntCntrl.AddEventHandler<string>(GameEvent.Item_UseTool, PlayUseTool);
        evntCntrl.AddEventHandler<object>(GameEvent.Item_ReleaseBow, PlayReleaseBow);
        evntCntrl.AddEventHandler<object>(GameEvent.Item_DrawBow, PlayDrawBow);
        
        IsShaking = false;
        IsSwimming = false;
    }
    public void PlayAnimatorMove(float3 normVel) {
        float speed = Mathf.Lerp(animator.GetFloat("Speed"), (float)math.length(normVel), 0.35f);
        float angleTheta = Mathf.Atan2(normVel.z, normVel.x) / Mathf.PI;
        angleTheta = Mathf.Lerp(animator.GetFloat("MoveTheta"), angleTheta, 0.35f);
        
        animator.SetFloat("Speed", speed);
        animator.SetFloat("MoveTheta", angleTheta);
    }

    private void PlayConsume(object source, object target, ref float amount) => animator.SetTrigger("Eat");
    private void PlayPlace(object source, object target, ref float3 terrPt) => animator.SetTrigger("Place");
    private void PlayUseTool(object source, object target, ref string anim) => animator.SetTrigger(anim);
    private void PlayStand(object source, object target, ref object _) => animator.SetBool("IsSitting", false);
    private void PlayDead(object source, object target, ref object _) => animator.SetBool("IsDead", true);
    private void PlayJump(object source, object target, ref float3 force) => animator.SetBool("IsJumping", true);
    private void PlayDrawBow(object source, object target, ref object _) {
        animator.SetBool("IsDrawingBow", true);
        if (HeldItem?.TryGetComponent<Animator>(out Animator bowAnim) == null)
            return;
        bowAnim.SetBool("IsDrawingBow", true);
    }
    private void PlayReleaseBow(object source, object target, ref object _) {
        animator.SetBool("IsDrawingBow", false);
        if (HeldItem == null || !HeldItem.TryGetComponent(out Animator bowAnim))
            return;
        bowAnim.SetBool("IsDrawingBow", false);

    }
    private void DeserializeAnimator(object source, object target, ref object _) {
        if (!(source as IAttackable).IsDead) return;
        PlayDead(source, target, ref _);
    }

    private void PlayStopSwim(object source, object target, ref float density) {
        animator.SetBool("IsSwimming", false);

        if (!IsSwimming) return;
        IsSwimming = false;
        Indicators.PlayWaterSplash(source as Entity, Config.CURRENT.GamePlay.Player.value.Physicality.value.weight);
    }
    private void PlayStartSwim(object source, object target, ref float density) {
        animator.SetBool("IsSwimming", true);
        animator.SetBool("IsJumping", false);
        animator.SetBool("IsSitting", false);

        if (IsSwimming) return;
        IsSwimming = true;
        Indicators.PlayWaterSplash(source as Entity, Config.CURRENT.GamePlay.Player.value.Physicality.value.weight);
    }

    private void PlaySit(object source, object target, ref object _) {
        animator.SetBool("IsSitting", true);
        animator.SetBool("IsJumping", false);
        animator.SetBool("IsSwimming", false);
    }
    private void PlayPunch(object source, object target, ref (float damage, float3 knockback)prms) {
        string anim = UnityEngine.Random.Range(0, 2) == 0 ? "PunchR" : "PunchL";
        animator.SetTrigger(anim);
    }
    private void PlayTouchdown(object source, object target, ref float zVelDelta) {
        if((source as IAttackable).IsDead) return;
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (!state.IsName("hRig_fall")) return;
        animator.SetBool("IsJumping", false);
    }

    private void PlayLookGradual(object source, object target, ref (float pitch, float deltaYaw)prms) {
        animator.SetBool("DisableShuffle", false);
        animator.SetFloat("LookY", prms.pitch);
        float yaw = animator.GetFloat("LookX");
        yaw = (yaw + prms.deltaYaw) * 0.75f;
        animator.SetFloat("LookX", yaw);
    }

    private void PlayLookDirect(object source, object target, ref (float pitch, float yaw)prms) {
        animator.SetBool("DisableShuffle", true);
        animator.SetFloat("LookY", prms.pitch);
        animator.SetFloat("LookX", prms.yaw);
    }

    private void PlayDamaged(object source, object target, ref (float damage, float3 knockback)prms) {
        if ((source as IAttackable).IsDead) return;
        OctreeTerrain.MainCoroutines.Enqueue(CameraShake(0.2f, 0.25f));

        float3 knockback = prms.knockback; //copy
        quaternion WSToOS = math.inverse(PlayerHandler.data.transform.rotation);
        knockback = math.mul(WSToOS, knockback);
        if (math.lengthsq(knockback) < 1E-6) return;
        knockback = math.normalize(knockback);
        animator.SetFloat("KnockZ", knockback.z);
        float xStr = 1 - math.abs(knockback.z);
        animator.SetFloat("KnockX", knockback.x * xStr);
        float yStr = (1 - math.abs(knockback.z)) * xStr;
        animator.SetFloat("KnockY", knockback.y * yStr);
    }

    private void PlayHoldItem(object source, object target, ref GameObject model) {
        Transform handle = player.transform.Find("Player/hRig/spine/spine.001/spine.002/spine.003/shoulder.L/upper_arm.L/forearm.L/hand.L/handle");
        HeldItem = GameObject.Instantiate(model, handle, false);
    }

    private void PlayUnHoldItem(object source, object target, ref GameObject model) {
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
