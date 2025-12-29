using UnityEngine;
using FMODUnity;
using FMOD.Studio;
using System.Collections.Generic;
using Arterra.Core.Storage;

public class AudioManager : MonoBehaviour {
    public static AudioManager Instance;
    [SerializeField]
    private EventReference AmbienceMusicEvent;
    [SerializeField]
    private EventReference WaterSplashEvent;

    private EventInstance ambience;
    private Bus sfxBus;

    void Awake() {
        if (Instance == null) {
            Instance = this; 
            DontDestroyOnLoad(gameObject); 
        } else {
            Destroy(gameObject); 
        }
    }


    void Start() {
        ambience = RuntimeManager.CreateInstance(AmbienceMusicEvent);
        ambience.start();
    }

    void OnDestroy()
    {
        ambience.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        ambience.release();
    }

    public void Initialize() {
        sfxBus = RuntimeManager.GetBus("bus:/SFX");
        UpdateAmbience(0);
    }

    public void Release() {
        sfxBus.stopAllEvents(FMOD.Studio.STOP_MODE.IMMEDIATE);
        UpdateAmbience(0);
    }

    private static EventInstance CreateEvent(EventReference evt) {
        EventInstance inst = RuntimeManager.CreateInstance(evt);
        inst.start();
        inst.release();
        return inst;
    }

    private static EventInstance CreateEventAttached(EventReference evt, Vector3 pos) {
        EventInstance inst = RuntimeManager.CreateInstance(evt);
        inst.set3DAttributes(RuntimeUtils.To3DAttributes(pos));
        inst.start();
        inst.release();
        return inst;
    }
    public void UpdateAmbience(float gameState) => ambience.setParameterByName("Game State", gameState);

    public static void PlayWaterSplash(Arterra.Config.Generation.Entity.Entity entity, byte contact, float weight = 1) {
        const int small = 0; const int medium = 1;  const int large = 2; 
        float strength = Unity.Mathematics.math.length(entity.velocity);
        if (strength <= 4) return;
        strength *= weight;

        int type = strength < 7.5 ? small : strength < 15 ? medium : large;
        EventInstance evnt = CreateEventAttached(Instance.WaterSplashEvent, CPUMapManager.GSToWS(entity.position));
        evnt.setParameterByName("Splash Strength", (float)type);
    }


    
}
