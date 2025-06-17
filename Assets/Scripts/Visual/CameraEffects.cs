using UnityEngine;
using System.Collections;

public class CameraEffects
{
    private bool IsShaking = false;
    public CameraEffects(){
        IsShaking = false;
    } 
    //TODO: Move this to it's own effects file
    public IEnumerator CameraShake(float duration, float rStrength)
    {
        if (IsShaking) yield break; //Stop
        IsShaking = true;

        Transform CameraLocalT = PlayerHandler.camera.GetChild(0).transform;
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
