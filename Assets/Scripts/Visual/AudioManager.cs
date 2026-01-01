using UnityEngine;
using FMODUnity;
using FMOD.Studio;
using System.Collections.Generic;
using System;
using System.Linq;
using Unity.Mathematics;
using Arterra.Core.Storage;

enum AudioEventsBase {
    SFXItem = 2000,
    SFXEntity = 3000,
    SFXAction = 4000,
}
public enum AudioEvents {
    None = 0,
    Item_PlainArrowFire = AudioEventsBase.SFXItem + 1,

    Entity_PlainArrowFlyby = AudioEventsBase.SFXEntity + 0,
    Entity_PlainArrowHit = AudioEventsBase.SFXEntity + 1,

    Action_WaterSplash = AudioEventsBase.SFXAction + 0,
}
public class AudioManager : MonoBehaviour {
    public static AudioManager Instance;
    [SerializeField]
    private EventReference AmbienceMusicEvent;
    [SerializeField]
    private List<AudioEventReference> AudioReferences;
    private Dictionary<AudioEvents, EventReference> Reference;

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
        Reference = AudioReferences.ToDictionary(s => s.name, s => s.reference);
        sfxBus = RuntimeManager.GetBus("bus:/SFX");
        UpdateAmbience(0);
    }

    public void Release() {
        sfxBus.stopAllEvents(FMOD.Studio.STOP_MODE.IMMEDIATE);
        UpdateAmbience(0);
    }

    public static EventInstance CreateEvent(AudioEvents evt, Vector3 pos = default) {
        if (!Instance.Reference.TryGetValue(evt, out EventReference reference))
            return default;
        EventInstance inst = RuntimeManager.CreateInstance(reference);
        float3 positionWS = CPUMapManager.GSToWS(pos);
        inst.set3DAttributes(RuntimeUtils.To3DAttributes(positionWS));
        inst.start();
        inst.release();
        return inst;
    }

    public static EventInstance CreateEventAttached(AudioEvents evt, GameObject obj)
    {
        if (!Instance.Reference.TryGetValue(evt, out EventReference reference))
            return default;

        EventInstance inst = RuntimeManager.CreateInstance(reference);
        RuntimeManager.AttachInstanceToGameObject(inst, obj);

        inst.start();
        inst.release();
        return inst;
    }   

    public static void StopEvent(EventInstance inst, bool allowFadeOut = true)
    {
        if (!inst.isValid())
            return;

        inst.stop(allowFadeOut
            ? FMOD.Studio.STOP_MODE.ALLOWFADEOUT
            : FMOD.Studio.STOP_MODE.IMMEDIATE);

        inst.release();
    }


    public void UpdateAmbience(float gameState) => ambience.setParameterByName("Game State", gameState);

    [Serializable]
    public struct AudioEventReference {
        public AudioEvents name;
        public EventReference reference;
    }
    
}
