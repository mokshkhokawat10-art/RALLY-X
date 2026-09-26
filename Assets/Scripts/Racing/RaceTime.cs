using UnityEngine;

namespace RallyX.Racing
{
    /// <summary>Text formatting for race times and positions.</summary>
    public static class RaceTime
    {
        /// <summary>Minutes, seconds and milliseconds, e.g. 1:23.456. Minutes keep counting past 59.</summary>
        public static string Format(float seconds)
        {
            int milliseconds = Mathf.RoundToInt(Mathf.Max(0f, seconds) * 1000f);
            return $"{milliseconds / 60000}:{milliseconds / 1000 % 60:00}.{milliseconds % 1000:000}";
        }

        /// <summary>1st, 2nd, 3rd, 4th, ... 11th, 12th, 13th, ... 21st.</summary>
        public static string Ordinal(int position)
        {
            int lastTwo = position % 100;
            string suffix = lastTwo is >= 11 and <= 13
                ? "th"
                : (position % 10) switch
                {
                    1 => "st",
                    2 => "nd",
                    3 => "rd",
                    _ => "th",
                };
            return position + suffix;
        }
    }
}
