using UnityEngine;
using FMODUnity;
using FMOD.Studio;
public class AudioManager : MonoBehaviour {

    public static AudioManager Instance;
    [SerializeField]
    private EventReference AmbienceMusicEvent;
    private EventInstance ambience;

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

    public void UpdateAmbience(float gameState) {
        ambience.setParameterByName("Game State", gameState);      
    }

    void OnDestroy()
    {
        ambience.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        ambience.release();
    }
}
