using System;
using System.IO;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Rendering;
using Arterra.Configuration;
using Arterra.Core.Storage;
using Arterra.Core.Terrain;
using Arterra.Core.Player;

namespace Arterra.Configuration.Gameplay{
    /// <summary> Settings controlling environment constants of the world. Or aspects of 
    /// the world that cannot be directly influenced within the game's narrative.
    /// We try to reduce the amount of non-narrative environment settings to keep the 
    /// game as immersive and open as possible. </summary>
    [Serializable]
    public struct Environment{
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
        [UISetting(Ignore = true)]
        public Option<AnimationCurve> sunIntensityCurve;
        /// <summary> How the intensity of the moon's light changes over the course of the day. The x axis is the percentage through the day, with 0 and 1
        /// being <see cref="sunriseHour"/> exactly a day apart. The y axis is the intensity of the moon's light as a percentage of <see cref="maxMoonIntensity"/>.</summary>
        [UISetting(Ignore = true)]
        public Option<AnimationCurve> moonIntensityCurve;
    }
}

public class WorldData {
    public DateTime currentTime;
    public Arterra.UI.ToolTips.ToolTipSystemState ToolTips;
    public static WorldData Build() {
        WorldData d = new WorldData();
        d.currentTime = DateTime.Now.Date + TimeSpan.FromHours(Config.CURRENT.GamePlay.Time.value.startHour);
        d.ToolTips = Arterra.UI.ToolTips.ToolTipSystemState.Build();
        return d;
    }
}
/// <summary> Controls static information about the world
/// that is not tied to the player. Currently just
/// handles day night information. </summary>
public static class WorldDataHandler
{ 
    public static WorldData WorldData;
    private static Arterra.Configuration.Gameplay.Environment settings =>  Config.CURRENT.GamePlay.Time;
    private static LensFlareComponentSRP sunFlare;
    private static Light Sun;
    private static Light Moon;
    private static IUpdateSubscriber eventTask;

    // Start is called before the first frame update
    public static void Initialize()
    {
        Sun = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Sun")).GetComponent<Light>();
        Moon = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Moon")).GetComponent<Light>();
        LoadWorldData();

        var constraintSource = new ConstraintSource { sourceTransform = OctreeTerrain.viewer, weight = 1 };
        Sun.GetComponent<PositionConstraint>().SetSource(0, constraintSource);
        Moon.GetComponent<PositionConstraint>().SetSource(0, constraintSource);
        sunFlare = Sun.GetComponent<LensFlareComponentSRP>();
        eventTask = new IndirectUpdate(Update);
        OctreeTerrain.MainLoopUpdateTasks.Enqueue(eventTask);
    }

    public static void Release() => SaveWorldData();

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
        WorldData.currentTime = WorldData.currentTime.AddSeconds(Time.deltaTime * settings.timeMultiplier);
        float progress = GetDayProgress(WorldData.currentTime.TimeOfDay);
        UpdateSunFlareShimmer();

        float rotation = Mathf.Lerp(0, 360, progress);
        Sun.transform.rotation = Quaternion.AngleAxis(rotation, Vector3.right);
        Moon.transform.rotation = Quaternion.AngleAxis((rotation + 180)%360, Vector3.right);
        UpdateLightSettings(progress);
    }

    private static void UpdateSunFlareShimmer(){
        const float ShimmerRate = 50f;
        float angleDiff = Vector3.Dot(sunFlare.transform.forward, PlayerHandler.data.Forward);
        float pulse = Mathf.Sin(angleDiff * ShimmerRate) * 0.5f + 0.5f;
        sunFlare.lensFlareData.elements[2].uniformScale = 2.5f + 2.5f * pulse;
        sunFlare.lensFlareData.elements[3].uniformScale = 5 + 2.5f * (1-pulse);
    }

    private static void UpdateLightSettings(float progress)
    {
        Sun.intensity = Mathf.Lerp(0, settings.maxSunIntensity, settings.sunIntensityCurve.value.Evaluate(progress));
        Moon.intensity = Mathf.Lerp(0, settings.maxMoonIntensity, settings.moonIntensityCurve.value.Evaluate(progress));
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
