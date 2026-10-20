using System;
using System.IO;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Rendering;
using Arterra.Configuration;
using Arterra.Core.Storage;
using Arterra.Engine.Rendering;
using Arterra.Engine.Terrain;
using Arterra.GamePlay;
using Arterra.Core;
using Arterra.Core.Network;

namespace Arterra.Configuration.Gameplay{
    /// <summary> Settings controlling environment constants of the world. Or aspects of 
    /// the world that cannot be directly influenced within the game's narrative.
    /// We try to reduce the amount of non-narrative environment settings to keep the 
    /// game as immersive and open as possible. </summary>
    [Serializable]
    public struct Environment{
        [UIModifiable(CallbackName = "Environment:TimeMultiplier")]
        /// <summary> How fast time progresses in the game world relative to real time. </summary>
        public float timeMultiplier;
        /// <summary> The time of day that the world starts at. The time in hours since midnight that
        /// the player starts at when they first enter the world. </summary>
        public float startHour;
        /// <summary> The time of day that the day starts at. The time in hours since midnight of the sunrise,
        /// or the time when the sun is parallel to the horizon on its upper arc. </summary>
        public float sunriseHour;
        /// <summary> The time of day that the day ends at. The time in hours since midnight of the sunset,
        /// or the time when the sun is parallel to the horizon on its lower arc. </summary>
        public float sunsetHour;
        /// <summary>  The maximum intensity of the sun light. This is the intensity of the light when the sun is at its peak. </summary>
        public float maxSunIntensity;
        /// <summary> The maximum intensity of the moon light. This is the intensity of the light when the moon is at its peak. </summary>
        public float maxMoonIntensity;
        /// <summary> How the intensity of the sun's light changes over the course of the day. The x axis is the percentage through the day, with 0 and 1
        /// being <see cref="sunriseHour"/> exactly a day apart. The y axis is the intensity of the sun's light as a percentage of <see cref="maxSunIntensity"/>.</summary>
        [UIHidden]
        public Option<AnimationCurve> sunIntensityCurve;
        /// <summary> How the intensity of the moon's light changes over the course of the day. The x axis is the percentage through the day, with 0 and 1
        /// being <see cref="sunriseHour"/> exactly a day apart. The y axis is the intensity of the moon's light as a percentage of <see cref="maxMoonIntensity"/>.</summary>
        [UIHidden]
        public Option<AnimationCurve> moonIntensityCurve;
        /// <summary> Whether or not entity collision is resolved using soft collision instead of hard collision. 
        /// Soft collision is more akin to minecraft style entity collision. </summary>
        public bool useSoftCollisions;
    }
}

public class WorldData {
    public TimeSpan timeOffset;
    public Arterra.GamePlay.UI.ToolTips.ToolTipSystemState ToolTips;
    public static WorldData Build() {
        WorldData d = new WorldData();
        d.timeOffset = TimeSpan.Zero;
        d.ToolTips = Arterra.GamePlay.UI.ToolTips.ToolTipSystemState.Build();
        return d;
    }
}
/// <summary> Controls static information about the world
/// that is not tied to the player. Currently just
/// handles day night information. </summary>
public static class WorldDataHandler
{ 
    public static WorldData WorldData;
    private static NetworkVariableBase<TimeAnchor> clock;
    private static DateTime gameStartTime;

    private struct TimeAnchor {
        public TimeSpan Offset;
        public DateTime StartTime;
        public float Multiplier;

        public TimeSpan At(DateTime now) => Offset + (now - StartTime) * Multiplier;
    }

    public static DateTime CurrentTime => gameStartTime + clock.Value.At(DateTime.UtcNow);

    private static void OnTimeMultiplierChanged(ref object value) {
        if (!NetworkManager.IsActingServer) {
            value = clock.HasValue ? clock.Value.Multiplier : settings.timeMultiplier;
            return;
        }

        float multiplier = (float)value;
        if (multiplier == clock.Value.Multiplier) return;
        DateTime now = DateTime.UtcNow;
        clock.Value = new TimeAnchor {
            Offset = clock.Value.At(now),
            StartTime = now,
            Multiplier = multiplier
        };
    }

    private static Arterra.Configuration.Gameplay.Environment settings =>  Config.CURRENT.GamePlay.Environment;
    private static LensFlareComponentSRP sunFlare;
    private static LensFlareComponentSRP moonFlare;
    private static Light Sun;
    private static Light Moon;
    private static ArterraRuntime.IUpdateSubscriber eventTask;
    private static float sunFlareBaseIntensity = 1.0f;
    private static float moonFlareBaseIntensity = 1.0f;

    // Start is called before the first frame update
    public static void Initialize()
    {
        Sun = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Sun")).GetComponent<Light>();
        Moon = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Moon")).GetComponent<Light>();
        LoadWorldData();

        gameStartTime = DateTime.UnixEpoch + TimeSpan.FromHours(settings.startHour);
        clock = new NetworkVariableBase<TimeAnchor>(
            NetworkReferenceId.FromName("WorldData:Time"), retainValueOnDisconnect: true);
        if (NetworkManager.IsActingServer) {
            clock.Value = new TimeAnchor {
                Offset = WorldData.timeOffset,
                StartTime = DateTime.UtcNow,
                Multiplier = settings.timeMultiplier
            };
        }
        Config.CURRENT.System.AddHook("Environment:TimeMultiplier", OnTimeMultiplierChanged);

        var constraintSource = new ConstraintSource { sourceTransform = OctreeTerrain.viewer, weight = 1 };
        Sun.GetComponent<PositionConstraint>().SetSource(0, constraintSource);
        Moon.GetComponent<PositionConstraint>().SetSource(0, constraintSource);
        sunFlare = Sun.GetComponent<LensFlareComponentSRP>();
        moonFlare = Moon.GetComponent<LensFlareComponentSRP>();
        sunFlareBaseIntensity = sunFlare != null ? sunFlare.intensity : 1.0f;
        moonFlareBaseIntensity = moonFlare != null ? moonFlare.intensity : 1.0f;
        eventTask = new ArterraRuntime.IndirectUpdate(Update);
        ArterraRuntime.MainLoopUpdateTasks.Enqueue(eventTask);
    }

    public static void Release() {
        if (eventTask != null) eventTask.Active = false;
        Config.CURRENT.System.RemoveHook("Environment:TimeMultiplier", OnTimeMultiplierChanged);
        if (clock != null) {
            if (clock.HasValue) WorldData.timeOffset = clock.Value.At(DateTime.UtcNow);
            clock.Dispose();
            clock = null;
        }
        SaveWorldData();
    }

    static void LoadWorldData(){
        string path = World.WORLD_SELECTION.First.Value.Path + "/WorldData.json";
        if(!File.Exists(path)) { 
            WorldData = WorldData.Build(); 
        } else {
            string data = File.ReadAllText(path);
            WorldData = Newtonsoft.Json.JsonConvert.DeserializeObject<WorldData>(data);   
        }
    }

    static void SaveWorldData(){
        string path = World.WORLD_SELECTION.First.Value.Path + "/WorldData.json";
        using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None)){
            using StreamWriter writer = new StreamWriter(fs);
            string data = Newtonsoft.Json.JsonConvert.SerializeObject(WorldData);
            writer.Write(data);
            writer.Flush();
        };
    }

    // Update is called once per frame
    public static void Update(MonoBehaviour mono)
    {
        if (!clock.HasValue) return;
        float progress = GetDayProgress(CurrentTime.TimeOfDay);

        float rotation = Mathf.Lerp(0, 360, progress);
        Sun.transform.rotation = Quaternion.AngleAxis(rotation, Vector3.right);
        Moon.transform.rotation = Quaternion.AngleAxis((rotation + 180)%360, Vector3.right);
        UpdateLightSettings(progress);
        UpdateSunFlareShimmer();
        UpdateFlareBrightnessFromHeadLuminance();
    }

    private static void UpdateSunFlareShimmer(){
        if (sunFlare == null || Sun == null)
            return;

        Camera cam = Camera.main;
        if (cam == null)
            return;

        LensFlareDataSRP flareData = sunFlare.lensFlareData;
        if (flareData == null || flareData.elements == null || flareData.elements.Length <= 3)
            return;

        // Direction from viewer toward the sun disk for a directional light.
        Vector3 toSun = -Sun.transform.forward;
        float alignment = Mathf.Clamp01(Vector3.Dot(cam.transform.forward, toSun));
        float angleResponse = alignment * alignment * (3.0f - 2.0f * alignment);

        flareData.elements[2].uniformScale = Mathf.Lerp(1.8f, 5.0f, angleResponse);
        flareData.elements[3].uniformScale = Mathf.Lerp(7.2f, 3.8f, angleResponse);
    }

    private static void UpdateLightSettings(float progress)
    {
        Sun.intensity = Mathf.Lerp(0, settings.maxSunIntensity, settings.sunIntensityCurve.value.Evaluate(progress));
        Moon.intensity = Mathf.Lerp(0, settings.maxMoonIntensity, settings.moonIntensityCurve.value.Evaluate(progress));
    }

    private static void UpdateFlareBrightnessFromHeadLuminance()
    {
        if (PlayerHandler.data == null || Sun == null || Moon == null)
            return;

        bool sunDominant = Sun.intensity >= Moon.intensity;
        Vector3 lightDirection = sunDominant ? -Sun.transform.forward : -Moon.transform.forward;

        Vector3 headWS = PlayerHandler.data.head;
        if (!AtmospherePass.TrySampleSingleOriginLuminance(headWS, lightDirection, out Vector3 sampledLuminance))
            return;
        //Standard Luminance(Y) Coeffs
        float luminanceScalar = Mathf.Clamp01(Vector3.Dot(sampledLuminance, new Vector3(0.2126f, 0.7152f, 0.0722f))); 

        if (sunFlare != null) sunFlare.intensity = sunFlareBaseIntensity * (sunDominant ? luminanceScalar : 1.0f);
        if (moonFlare != null) moonFlare.intensity = moonFlareBaseIntensity * (sunDominant ? 1.0f : luminanceScalar);
    }

    public static float GetDayProgress(TimeSpan TimeOfDay)
    {
        //These settings are volatile
        TimeSpan sunriseTime = TimeSpan.FromHours(settings.sunriseHour);
        TimeSpan sunsetTime = TimeSpan.FromHours(settings.sunsetHour);
        if (TimeOfDay > sunriseTime && TimeOfDay < sunsetTime)
        {
            TimeSpan dayDuration = CalculateTimeDiff(sunriseTime, sunsetTime);
            TimeSpan timeSinceSunrise = CalculateTimeDiff(sunriseTime, TimeOfDay);

            return Mathf.Lerp(0, 0.5f, (float)(timeSinceSunrise.TotalSeconds / dayDuration.TotalSeconds));
        }
        else
        {
            TimeSpan nightDuration = CalculateTimeDiff(sunsetTime, sunriseTime);
            TimeSpan timeSinceSunset = CalculateTimeDiff(sunsetTime, TimeOfDay);

            return Mathf.Lerp(0.5f, 1, (float)(timeSinceSunset.TotalSeconds / nightDuration.TotalSeconds));
        }
    }

    public static TimeSpan CalculateTimeDiff(TimeSpan from, TimeSpan to)
    {
        TimeSpan diff = to - from;

        if (diff.TotalSeconds < 0)
            diff += TimeSpan.FromHours(24);

        return diff;
    }
    
}
