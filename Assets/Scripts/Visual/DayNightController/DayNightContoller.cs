using System;
using UnityEngine;
using UnityEngine.Animations;

public class DayNightContoller : UpdateTask
{
    private static Settings settings =>  WorldOptions.CURRENT.GamePlay.DayNightCycle;
    [Serializable]
    public struct Settings{
        public float timeMultiplier;
        public float startHour;
        public float sunriseHour;
        public float sunsetHour;
        public float maxSunIntensity;
        public float maxMoonIntensity;
        [UISetting(Ignore = true)]
        public Option<AnimationCurve> sunIntensityCurve;
        [UISetting(Ignore = true)]
        public Option<AnimationCurve> moonIntensityCurve;
    }

    public static DateTime currentTime;
    private static Light Sun;
    private static Light Moon;

    // Start is called before the first frame update
    public static void Initialize()
    {
        Sun = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Sun")).GetComponent<Light>();
        Moon = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Moon")).GetComponent<Light>();
        var constraintSource = new ConstraintSource { sourceTransform = TerrainGeneration.OctreeTerrain.viewer, weight = 1 };
        Sun.GetComponent<PositionConstraint>().SetSource(0, constraintSource);
        Moon.GetComponent<PositionConstraint>().SetSource(0, constraintSource);
        TerrainGeneration.OctreeTerrain.MainLoopUpdateTasks.Enqueue(new DayNightContoller{active = true});
    }

    // Update is called once per frame
    public override void Update(MonoBehaviour mono)
    {
        currentTime = currentTime.AddSeconds(Time.deltaTime * settings.timeMultiplier);
        float progress = GetDayProgress(currentTime.TimeOfDay);

        float rotation = Mathf.Lerp(0, 360, progress);
        Sun.transform.rotation = Quaternion.AngleAxis(rotation, Vector3.right);
        Moon.transform.rotation = Quaternion.AngleAxis((rotation + 180)%360, Vector3.right);
        UpdateLightSettings(progress);
    }

    private void UpdateLightSettings(float progress)
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
