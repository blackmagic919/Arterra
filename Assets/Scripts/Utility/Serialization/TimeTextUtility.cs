using System;

namespace Arterra.Utils {
    public static class TimeTextUtilty {
        public static string ToTimeAgo(this DateTime dateTime)
        {
            var ts = DateTime.UtcNow - dateTime.ToUniversalTime();

            double seconds = ts.TotalSeconds;
            double minutes = ts.TotalMinutes;
            double hours   = ts.TotalHours;
            double days    = ts.TotalDays;

            if (seconds < 60)
                return $"{(int)seconds}s ago";

            if (minutes < 60)
                return $"{(int)minutes}m ago";

            if (hours < 24)
                return $"{(int)hours}hr ago";

            if (days < 7)
                return $"{(int)days} days ago";

            if (days < 30)
                return $"{(int)(days / 7)} weeks ago";

            if (days < 365)
                return $"{(int)(days / 30)} months ago";

            return $"{(int)(days / 365)} years ago";
        }   
    }
}