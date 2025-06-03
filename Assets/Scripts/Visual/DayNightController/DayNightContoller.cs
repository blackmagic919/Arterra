using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Rendering;
using WorldConfig;

namespace WorldConfig.Gameplay{
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
public static class DayNightContoller
{
    private static WorldConfig.Gameplay.Environment settings =>  Config.CURRENT.GamePlay.Time;
    public static DateTime currentTime{
        get => PlayerHandler.data.currentTime;
        set => PlayerHandler.data.currentTime = value;
    }
    private static LensFlareComponentSRP sunFlare;
    private static Light Sun;
    private static Light Moon;
    private static UpdateTask eventTask;

    // Start is called before the first frame update
    public static void Initialize()
    {
        Sun = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Sun")).GetComponent<Light>();
        Moon = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Moon")).GetComponent<Light>();
        var constraintSource = new ConstraintSource { sourceTransform = TerrainGeneration.OctreeTerrain.viewer, weight = 1 };
        Sun.GetComponent<PositionConstraint>().SetSource(0, constraintSource);
        Moon.GetComponent<PositionConstraint>().SetSource(0, constraintSource);
        sunFlare = Sun.GetComponent<LensFlareComponentSRP>();
        eventTask = new IndirectUpdate(Update);
        TerrainGeneration.OctreeTerrain.MainLoopUpdateTasks.Enqueue(eventTask);
    }

    // Update is called once per frame
    public static void Update(MonoBehaviour mono)
    {
        currentTime = currentTime.AddSeconds(Time.deltaTime * settings.timeMultiplier);
        float progress = GetDayProgress(currentTime.TimeOfDay);
        UpdateSunFlareShimmer();

        float rotation = Mathf.Lerp(0, 360, progress);
        Sun.transform.rotation = Quaternion.AngleAxis(rotation, Vector3.right);
        Moon.transform.rotation = Quaternion.AngleAxis((rotation + 180)%360, Vector3.right);
        UpdateLightSettings(progress);
    }

    private static void UpdateSunFlareShimmer(){
        const float ShimmerRate = 50f;
        float angleDiff = Vector3.Dot(sunFlare.transform.forward, PlayerHandler.camera.transform.forward);
        float pulse = Mathf.Sin(angleDiff * ShimmerRate) * 0.5f + 0.5f;
        sunFlare.lensFlareData.elements[2].uniformScale = 5 + 5 * pulse;
        sunFlare.lensFlareData.elements[3].uniformScale = 10 + 5 * (1-pulse);
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
