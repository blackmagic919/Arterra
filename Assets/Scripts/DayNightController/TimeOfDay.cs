using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class TimeOfDay
{
    // Start is called before the first frame update
    private static TimeSpan sunriseTime;
    private static TimeSpan sunsetTime;
    private static float progress = 0;

    public static void Initialize(TimeSpan sunriseTS, TimeSpan sunsetTS)
    {
        sunriseTime = sunriseTS;
        sunsetTime = sunsetTS;
    }

    public static void UpdateProgress(TimeSpan TimeOfDay)
    {
        if (TimeOfDay > sunriseTime && TimeOfDay < sunsetTime)
        {
            TimeSpan dayDuration = CalculateTimeDiff(sunriseTime, sunsetTime);
            TimeSpan timeSinceSunrise = CalculateTimeDiff(sunriseTime, TimeOfDay);

            progress = Mathf.Lerp(0, 0.5f, (float)(timeSinceSunrise.TotalSeconds / dayDuration.TotalSeconds));
        }
        else
        {
            TimeSpan nightDuration = CalculateTimeDiff(sunsetTime, sunriseTime);
            TimeSpan timeSinceSunset = CalculateTimeDiff(sunsetTime, TimeOfDay);

            progress = Mathf.Lerp(0.5f, 1, (float)(timeSinceSunset.TotalSeconds / nightDuration.TotalSeconds));
        }
    }

    public static float GetProgress()
    {
        return progress;
    }

    public static TimeSpan CalculateTimeDiff(TimeSpan from, TimeSpan to)
    {
        TimeSpan diff = to - from;

        if (diff.TotalSeconds < 0)
            diff += TimeSpan.FromHours(24);

        return diff;
    }
}
