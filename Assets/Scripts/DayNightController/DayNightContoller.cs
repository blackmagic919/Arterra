using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEditor;
using AtmosphericHeightFog;

public class DayNightContoller : MonoBehaviour
{
    [SerializeField]
    private float timeMultiplier;
    [SerializeField]
    private float startHour;

    [SerializeField]
    private TextMeshProUGUI timeText;

    [SerializeField]
    private float sunriseHour;
    [SerializeField]
    private float sunsetHour;

    [SerializeField]
    private Light Sun;
    [SerializeField]
    private AnimationCurve sunLightIntensityCurve;
    [SerializeField]
    private float maxSunIntensity;

    [SerializeField]
    private Light Moon;
    [SerializeField]
    private AnimationCurve moonLightIntensityCurve;
    [SerializeField]
    private float maxMoonIntensity;

    [SerializeField]
    private HeightFogGlobal fogController;

    [SerializeField]
    private AnimationCurve fogSettingsCurve;

    private DateTime currentTime;

    private TimeSpan sunriseTime;
    private TimeSpan sunsetTime;

    private bool isDay;

    // Start is called before the first frame update
    void Start()
    {
        currentTime = DateTime.Now.Date + TimeSpan.FromHours(startHour);

        sunriseTime = TimeSpan.FromHours(sunriseHour);
        sunsetTime = TimeSpan.FromHours(sunsetHour);
        TimeOfDay.Initialize(sunriseTime, sunsetTime);
    }

    // Update is called once per frame
    void Update()
    {
        UpdateTimeOfDay();
        TimeOfDay.UpdateProgress(currentTime.TimeOfDay);

        float progress = TimeOfDay.GetProgress();

        float rotation = Mathf.Lerp(0, 360, progress);
        Sun.transform.rotation = Quaternion.AngleAxis(rotation, Vector3.right);
        Moon.transform.rotation = Quaternion.AngleAxis((rotation + 180)%360, Vector3.right);

        UpdateLightSettings(progress);
    }

    private void UpdateTimeOfDay()
    {
        currentTime = currentTime.AddSeconds(Time.deltaTime * timeMultiplier);

        if (timeText != null)
            timeText.text = currentTime.ToString("HH:mm");
    }

    private void UpdateLightSettings(float progress)
    {
        Sun.intensity = Mathf.Lerp(0, maxSunIntensity, sunLightIntensityCurve.Evaluate(progress));
        Moon.intensity = Mathf.Lerp(0, maxMoonIntensity, moonLightIntensityCurve.Evaluate(progress));
        fogController.timeOfDay = Mathf.Clamp(fogSettingsCurve.Evaluate(progress), 0, 1);
    }

    
}
