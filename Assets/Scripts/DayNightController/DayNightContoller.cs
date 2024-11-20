using System;
using UnityEngine;
using UnityEngine.Animations;
using static OctreeTerrain;

public class DayNightContoller : UpdateTask
{
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
    private static Settings s;

    public static DateTime currentTime;
    private static TimeSpan sunriseTime;
    private static TimeSpan sunsetTime;
    private static Light Sun;
    private static Light Moon;

    // Start is called before the first frame update
    public static void Initialize()
    {
        Sun = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Sun")).GetComponent<Light>();
        Moon = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Moon")).GetComponent<Light>();
        var constraintSource = new ConstraintSource { sourceTransform = OctreeTerrain.viewer, weight = 1 };
        Sun.GetComponent<PositionConstraint>().SetSource(0, constraintSource);
        Moon.GetComponent<PositionConstraint>().SetSource(0, constraintSource);
        s = WorldStorageHandler.WORLD_OPTIONS.GamePlay.DayNightCycle;

        sunriseTime = TimeSpan.FromHours(s.sunriseHour);
        sunsetTime = TimeSpan.FromHours(s.sunsetHour);
        TimeOfDay.Initialize(sunriseTime, sunsetTime);
        MainLoopUpdateTasks.Enqueue(new DayNightContoller{active = true});
    }

    // Update is called once per frame
    public override void Update(MonoBehaviour mono)
    {
        currentTime = currentTime.AddSeconds(Time.deltaTime * s.timeMultiplier);
        TimeOfDay.UpdateProgress(currentTime.TimeOfDay);

        float progress = TimeOfDay.GetProgress();

        float rotation = Mathf.Lerp(0, 360, progress);
        Sun.transform.rotation = Quaternion.AngleAxis(rotation, Vector3.right);
        Moon.transform.rotation = Quaternion.AngleAxis((rotation + 180)%360, Vector3.right);
        UpdateLightSettings(progress);
    }

    private void UpdateLightSettings(float progress)
    {
        Sun.intensity = Mathf.Lerp(0, s.maxSunIntensity, s.sunIntensityCurve.value.Evaluate(progress));
        Moon.intensity = Mathf.Lerp(0, s.maxMoonIntensity, s.moonIntensityCurve.value.Evaluate(progress));
    }

    
}
