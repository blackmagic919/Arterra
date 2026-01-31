using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using Arterra.Engine.Terrain;
using Arterra.GamePlay;
using Arterra.Configuration;
using Arterra.Core.Events;
using Arterra.Data.Entity;
using Arterra.Core.Storage;

namespace Arterra.GamePlay {}
public class PlayerActionEffects{
    private bool IsShaking = false;
    private bool IsSwimming = false;
    private GameObject player;
    private Animator animator;
    private GameObject HeldItem;
    public void Initialize(GameObject player, EventControl evntCntrl) {
        this.player = player;
        this.animator = player.transform.Find("Player").GetComponent<Animator>();
        evntCntrl.AddEventHandler(GameEvent.Action_Jump, PlayJump);
        evntCntrl.AddEventHandler(GameEvent.Item_ConsumeFood, PlayConsume);
        evntCntrl.AddEventHandler(GameEvent.Action_RemoveTerrain, PlayPlace);
        evntCntrl.AddEventHandler(GameEvent.Action_PlaceTerrain, PlayPlace);
        evntCntrl.AddEventHandler(GameEvent.Action_LookGradual, PlayLookGradual);
        evntCntrl.AddEventHandler(GameEvent.Action_LookDirect, PlayLookDirect);
        evntCntrl.AddEventHandler(GameEvent.System_Deserialize, DeserializeAnimator);
        evntCntrl.AddEventHandler(GameEvent.Entity_Death, PlayDead);
        evntCntrl.AddEventHandler(GameEvent.Entity_Damaged, PlayDamaged);
        evntCntrl.AddEventHandler(GameEvent.Entity_HitGround, PlayTouchdown);
        evntCntrl.AddEventHandler(GameEvent.Action_MountRideable, PlaySit);
        evntCntrl.AddEventHandler(GameEvent.Action_DismountRideable, PlayStand);
        evntCntrl.AddEventHandler(GameEvent.Entity_InLiquid, PlayStartSwim);
        evntCntrl.AddEventHandler(GameEvent.Entity_InGas, PlayStopSwim);
        evntCntrl.AddEventHandler(GameEvent.Entity_Attack, PlayPunch);
        evntCntrl.AddEventHandler(GameEvent.Item_HoldTool, PlayHoldItem);
        evntCntrl.AddEventHandler(GameEvent.Item_UnholdTool, PlayUnHoldItem);
        evntCntrl.AddEventHandler(GameEvent.Item_UseTool, PlayUseTool);
        evntCntrl.AddEventHandler(GameEvent.Item_ReleaseBow, PlayReleaseBow);
        evntCntrl.AddEventHandler(GameEvent.Item_DrawBow, PlayDrawBow);
        
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

    private void PlayConsume(object source, object target, object amount) => animator.SetTrigger("Eat");  //ctx : float
    private void PlayPlace(object source, object target, object modifyPt) => animator.SetTrigger("Place");  //ctx : float3
    private void PlayUseTool(object source, object target, object anim) => animator.SetTrigger((string)anim); //ctx : string
    private void PlayStand(object source, object target, object _) => animator.SetBool("IsSitting", false);
    private void PlayDead(object source, object target, object _) => animator.SetBool("IsDead", true);
    private void PlayJump(object source, object target, object force) => animator.SetBool("IsJumping", true); //ctx : float
    private void PlayDrawBow(object source, object target, object _) {
        animator.SetBool("IsDrawingBow", true);
        if (HeldItem?.TryGetComponent<Animator>(out Animator bowAnim) == null)
            return;
        bowAnim.SetBool("IsDrawingBow", true);
    }
    private void PlayReleaseBow(object source, object target, object _) {
        animator.SetBool("IsDrawingBow", false);
        if (HeldItem == null || !HeldItem.TryGetComponent(out Animator bowAnim))
            return;
        bowAnim.SetBool("IsDrawingBow", false);

    }
    private void DeserializeAnimator(object source, object target, object _) {
        if (!(source as IAttackable).IsDead) return;
        PlayDead(source, target, _);
    }

    private void PlayStopSwim(object source, object target, object density) { //ctx: float
        animator.SetBool("IsSwimming", false);

        if (!IsSwimming) return;
        IsSwimming = false;
        Indicators.PlayWaterSplash(source as Entity, Config.CURRENT.GamePlay.Player.value.Physicality.value.weight);
    }
    private void PlayStartSwim(object source, object target, object density) { //ctx: float
        animator.SetBool("IsSwimming", true);
        animator.SetBool("IsJumping", false);
        animator.SetBool("IsSitting", false);

        if (IsSwimming) return;
        IsSwimming = true;
        Indicators.PlayWaterSplash(source as Entity, Config.CURRENT.GamePlay.Player.value.Physicality.value.weight);
    }

    private void PlaySit(object source, object target, object _) {
        animator.SetBool("IsSitting", true);
        animator.SetBool("IsJumping", false);
        animator.SetBool("IsSwimming", false);
    }
    private void PlayPunch(object source, object target, object prms) {//ctx: (float damage, float3 knockback)
        string anim = UnityEngine.Random.Range(0, 2) == 0 ? "PunchR" : "PunchL";
        animator.SetTrigger(anim);
    }
    private void PlayTouchdown(object source, object target, object zVelDelta) { //ctx: float
        if((source as IAttackable).IsDead) return;
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (!state.IsName("hRig_fall")) return;
        animator.SetBool("IsJumping", false);
    }

    private void PlayLookGradual(object source, object target, object prms) { //ctx : (float pitch, float deltaYaw)
        (float pitch, float deltaYaw) = ((RefTuple<(float, float)>)prms).Value;
        animator.SetBool("DisableShuffle", false);
        animator.SetFloat("LookY", pitch);
        float yaw = animator.GetFloat("LookX");
        yaw = (yaw + deltaYaw) * 0.75f;
        animator.SetFloat("LookX", yaw);
    }

    private void PlayLookDirect(object source, object target, object prms) {
        (float pitch, float yaw) = ((RefTuple<(float, float)>)prms).Value;
        animator.SetBool("DisableShuffle", true);
        animator.SetFloat("LookY", pitch);
        animator.SetFloat("LookX", yaw);
    }

    private void PlayDamaged(object source, object target, object prms) { //ctx: (float damage, float3 knockback)
        (float damage, float3 knockback) = ((RefTuple<(float, float3)>)prms).Value;
        if ((source as IAttackable).IsDead) return;
        OctreeTerrain.MainCoroutines.Enqueue(CameraShake(0.2f, 0.25f));

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

    private void PlayHoldItem(object source, object target, object model) { //ctx: GameObject
        Transform handle = player.transform.Find("Player/hRig/spine/spine.001/spine.002/spine.003/shoulder.L/upper_arm.L/forearm.L/hand.L/handle");
        HeldItem = GameObject.Instantiate((GameObject)model, handle, false);
    }

    private void PlayUnHoldItem(object source, object target, object model) { //ctx: GameObject
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

