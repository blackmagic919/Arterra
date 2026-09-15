using UnityEngine;
using FMODUnity;
using FMOD.Studio;
using System.Collections.Generic;
using System;
using System.Linq;
using Unity.Mathematics;
using Arterra.Core.Storage;

namespace Arterra.Engine.Audio {
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

        Entity_LionRoar = AudioEventsBase.SFXEntity + 100,
        Entity_LionHurt = AudioEventsBase.SFXEntity + 101,
        Entity_LionAttack = AudioEventsBase.SFXEntity + 102,
        Entity_LionMate = AudioEventsBase.SFXEntity + 103,
        Entity_LionGrunt = AudioEventsBase.SFXEntity + 104,

        Entity_FoxGrunt = AudioEventsBase.SFXEntity + 110,
        Entity_FoxMate = AudioEventsBase.SFXEntity + 111,
        Entity_FoxHurt = AudioEventsBase.SFXEntity + 112,
        Entity_FoxAttack = AudioEventsBase.SFXEntity + 113,

        Entity_CoyoteGrunt = AudioEventsBase.SFXEntity + 120,
        Entity_CoyoteHowl = AudioEventsBase.SFXEntity + 121,
        Entity_CoyoteMate = AudioEventsBase.SFXEntity + 122,
        Entity_CoyoteAttack = AudioEventsBase.SFXEntity + 123,
        Entity_CoyoteWhine = AudioEventsBase.SFXEntity + 124,

        Entity_SnakeSlither = AudioEventsBase.SFXEntity + 130,
        Entity_SnakeHiss = AudioEventsBase.SFXEntity + 131,
        Entity_SnakeAttack = AudioEventsBase.SFXEntity + 132,

        Entity_ElephantFlap = AudioEventsBase.SFXEntity + 140,
        Entity_ElephantGrunt = AudioEventsBase.SFXEntity + 141,
        Entity_ElephantTrumpet = AudioEventsBase.SFXEntity + 142,
        Entity_ElephantHurt = AudioEventsBase.SFXEntity + 143,
        Entity_ElephantMate = AudioEventsBase.SFXEntity + 144,

        Entity_HorseAttack = AudioEventsBase.SFXEntity + 150,
        Entity_HorseHurt = AudioEventsBase.SFXEntity + 151,
        Entity_HorseNeigh = AudioEventsBase.SFXEntity + 152,
        Entity_HorseGrunt = AudioEventsBase.SFXEntity + 153,
        Entity_RabbitSqueak = AudioEventsBase.SFXEntity + 160,
        Entity_RabbitAttack = AudioEventsBase.SFXEntity + 161,
        Entity_RabbitHurt = AudioEventsBase.SFXEntity + 162,
        Entity_RabbitMate = AudioEventsBase.SFXEntity + 163,

        Entity_CamelGrunt = AudioEventsBase.SFXEntity + 170,
        Entity_CamelHurt = AudioEventsBase.SFXEntity + 171,
        Entity_CamelAttack = AudioEventsBase.SFXEntity + 172,
        Entity_CamelMate = AudioEventsBase.SFXEntity + 173,
        
        Entity_ZebraWhine = AudioEventsBase.SFXEntity + 180,
        Entity_ZebraHurt = AudioEventsBase.SFXEntity + 181,
        Entity_ZebraMate = AudioEventsBase.SFXEntity + 182,
        Entity_ZebraAttack = AudioEventsBase.SFXEntity + 183,

        Entity_WhaleSong = AudioEventsBase.SFXEntity + 200,
        Entity_WhaleHurt = AudioEventsBase.SFXEntity + 201,
        Entity_WhaleMate = AudioEventsBase.SFXEntity + 202,
        Entity_WhaleAttack = AudioEventsBase.SFXEntity + 203,
        Entity_WhaleBlow = AudioEventsBase.SFXEntity + 204,

        Entity_SparrowChirp = AudioEventsBase.SFXEntity + 400,
        Entity_SparrowAttack = AudioEventsBase.SFXEntity + 401,
        Entity_SparrowHurt = AudioEventsBase.SFXEntity + 402,

        Entity_HawkCry = AudioEventsBase.SFXEntity + 410,
        Entity_HawkChirp = AudioEventsBase.SFXEntity + 411,
        Entity_HawkHurt = AudioEventsBase.SFXEntity + 412,
        Entity_HawkAttack = AudioEventsBase.SFXEntity + 413,

        Entity_OwlHoot = AudioEventsBase.SFXEntity + 420,
        Entity_OwlAttack = AudioEventsBase.SFXEntity + 421,
        Entity_OwlHurt = AudioEventsBase.SFXEntity + 422,
        Entity_OwlMate = AudioEventsBase.SFXEntity + 423,

        Action_WaterSplash = AudioEventsBase.SFXAction + 0,
        Action_WaterSubmerged = AudioEventsBase.SFXAction + 1,
        Action_Damage = AudioEventsBase.SFXAction + 2, 
        Action_Chew = AudioEventsBase.SFXAction + 3,
        Action_SwimWater = AudioEventsBase.SFXAction + 4,
        Action_FlapWings = AudioEventsBase.SFXAction + 5,
        
        Action_StepGrass = AudioEventsBase.SFXAction + 100,
        Action_StepSand = AudioEventsBase.SFXAction + 101,
        Action_StepSnow = AudioEventsBase.SFXAction + 102,
        Action_StepStone = AudioEventsBase.SFXAction + 103,
        Action_StepWood = AudioEventsBase.SFXAction + 104,
        Action_StepDirt = AudioEventsBase.SFXAction + 105,
        Action_StepLeaf = AudioEventsBase.SFXAction + 106,
        Action_StepGravel = AudioEventsBase.SFXAction + 107,
        Action_StepMud = AudioEventsBase.SFXAction + 108,
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

        void OnDestroy() {
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

        public static EventInstance CreateEventAttached(AudioEvents evt, GameObject obj) {
            if (!Instance.Reference.TryGetValue(evt, out EventReference reference))
                return default;

            EventInstance inst = RuntimeManager.CreateInstance(reference);
            RuntimeManager.AttachInstanceToGameObject(inst, obj);

            inst.start();
            inst.release();
            return inst;
        }

        public static void StopEvent(EventInstance inst, bool allowFadeOut = true) {
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
}
