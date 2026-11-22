using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Edith.Navigation
{
    /// The headset's yaw over the last twenty seconds, so a phone reading taken a moment ago can be paired with where
    /// the head pointed at that moment. Yaw is Unity's: degrees about +Y, clockwise seen from above, like a compass.
    public sealed class YawHistory
    {
        readonly float[] times, yaws;
        int count, next;

        public YawHistory(int capacity = 256)
        {
            times = new float[capacity];
            yaws = new float[capacity];
        }

        public void Record(float time, float yaw)
        {
            times[next] = time;
            yaws[next] = yaw;
            next = (next + 1) % times.Length;
            count = Math.Min(count + 1, times.Length);
        }

        /// The recorded yaw nearest `time`, when one lies within `tolerance` seconds of it.
        public bool TryAt(float time, float tolerance, out float yaw)
        {
            yaw = 0f;
            var best = float.PositiveInfinity;
            for (var i = 0; i < count; i++)
            {
                var gap = Math.Abs(times[i] - time);
                if (gap < best)
                {
                    best = gap;
                    yaw = yaws[i];
                }
            }
            return best <= tolerance;
        }

        /// Whether the head kept within `degrees` of one direction from `from` to `to`, with yaws recorded in between.
        public bool Steady(float from, float to, float degrees)
        {
            var any = false;
            var first = 0f;
            float low = 0f, high = 0f;
            for (var i = 0; i < count; i++)
            {
                if (times[i] < from || times[i] > to) continue;
                if (!any)
                {
                    any = true;
                    first = yaws[i];
                    continue;
                }
                var off = NavMath.Wrap(yaws[i] - first);
                low = Math.Min(low, off);
                high = Math.Max(high, off);
            }
            return any && high - low <= degrees;
        }
    }

    /// Where true north lies in the headset's world. The Quest has no compass, so each phone reading gives one guess:
    /// the head's yaw when the reading was taken minus the phone's direction. A compass guess holds only while the
    /// phone points the way the head faces, so the compass is used just to find north: once Agreeing guesses in a row
    /// agree, north is locked and the compass is ignored, and turning the head away from the phone cannot move it.
    /// A walking course does not depend on how the phone is held, so a run of agreeing course guesses locks north
    /// too, and afterwards nudges it, or replaces it when it was plainly wrong.
    public sealed class NorthCalibration
    {
        public const int Agreeing = 3;
        /// Guesses in a run lie within this many degrees of each other.
        public const float CompassSpread = 15f, CourseSpread = 25f;
        /// A run of course guesses further than this from the locked north replaces it; a nearer one moves it by
        /// CourseBlend of the difference.
        public const float SnapDegrees = 30f, CourseBlend = 0.3f;

        readonly List<float> compass = new List<float>(), course = new List<float>();

        public bool Ready { get; private set; }

        /// North was set or confirmed by walking, which no way of holding the phone can fool.
        public bool Walked { get; private set; }

        /// The Unity yaw that points to true north.
        public float NorthYaw { get; private set; }

        /// A guess from the phone's compass. Ignored once north is locked.
        public void AddCompass(float northYaw)
        {
            if (Ready || !Agree(compass, northYaw, CompassSpread, out var mean)) return;
            NorthYaw = mean;
            Ready = true;
        }

        /// A guess from the GPS course while walking.
        public void AddCourse(float northYaw)
        {
            if (!Agree(course, northYaw, CourseSpread, out var mean)) return;
            var off = NavMath.Wrap(mean - NorthYaw);
            NorthYaw = !Ready || Mathf.Abs(off) > SnapDegrees ? mean : NavMath.Wrap(NorthYaw + off * CourseBlend);
            Ready = Walked = true;
        }

        public void Reset()
        {
            compass.Clear();
            course.Clear();
            Ready = Walked = false;
            NorthYaw = 0f;
        }

        /// Adds `guess` to the run in `recent`, starting the run again when it disagrees with any guess in it, and
        /// gives the run's mean once it is Agreeing long.
        static bool Agree(List<float> recent, float guess, float spread, out float mean)
        {
            mean = 0f;
            foreach (var kept in recent)
            {
                if (Mathf.Abs(NavMath.Wrap(kept - guess)) <= spread) continue;
                recent.Clear();
                break;
            }
            recent.Add(guess);
            if (recent.Count > Agreeing) recent.RemoveAt(0);
            if (recent.Count < Agreeing) return false;
            var off = 0f;
            foreach (var kept in recent) off += NavMath.Wrap(kept - guess);
            mean = NavMath.Wrap(guess + off / recent.Count);
            return true;
        }
    }

    public static class NavMath
    {
        /// Walking pace, for the minutes shown under the arrow.
        public const float WalkMetresPerMinute = 80f;
        /// A GPS course is trusted only at walking pace or faster; standing still it points anywhere.
        public const float CourseMinSpeed = 1f;
        /// A compass that reports a worse error than this is ignored.
        public const float WorstCompassDegrees = 40f;

        /// `degrees` in (-180, 180].
        public static float Wrap(float degrees)
        {
            degrees %= 360f;
            if (degrees > 180f) degrees -= 360f;
            if (degrees <= -180f) degrees += 360f;
            return degrees;
        }

        /// The Unity yaw the arrow should point along for a target at `bearing` degrees clockwise from true north.
        public static float ArrowYaw(float northYaw, float bearing) => Wrap(northYaw + bearing);

        /// The guess of north from a phone direction (a heading or a course) taken while the head pointed at `headYaw`.
        public static float NorthFrom(float headYaw, float phoneDegrees) => Wrap(headYaw - phoneDegrees);

        /// Whether a compass reading counts: one that reports no error, or an error within WorstCompassDegrees.
        public static bool CompassUsable(float headingAccuracy) => headingAccuracy <= WorstCompassDegrees;

        /// How far the chevron turns clockwise from pointing up the view, for a way to go along `arrowYaw` seen by a
        /// head facing `headYaw`: 0 straight ahead, 90 to the right, 180 behind.
        public static float ChevronTurn(float arrowYaw, float headYaw) => Wrap(arrowYaw - headYaw);

        /// "Turn left in 40 m", "Arrive in 60 m"; empty for a turn kind the arrow does not know.
        public static string ManeuverText(string kind, float metres)
        {
            var phrase = kind switch
            {
                "left" => "Turn left", "right" => "Turn right", "slight_left" => "Keep left",
                "slight_right" => "Keep right", "sharp_left" => "Turn sharp left", "sharp_right" => "Turn sharp right",
                "uturn" => "Turn around", "arrive" => "Arrive", _ => "",
            };
            if (phrase.Length == 0) return "";
            // Within 15 m the turn is here, so it reads as a plain instruction with no distance.
            if (metres < 15f) return phrase;
            return $"{phrase} in {Metres(metres)}";
        }

        /// "3 min · 240 m", "18 min · 1.4 km".
        public static string DistanceText(float metres) => $"{Minutes(metres)} · {Metres(metres)}";

        /// The one line under the place's name: the next turn and the walk left, "Turn left in 40 m · 3 min", or without a
        /// turn the walk left with its distance.
        public static string GuideText(string maneuver, float maneuverMetres, float walkMetres) =>
            ManeuverText(maneuver, maneuverMetres) is { Length: > 0 } turn
                ? $"{turn} · {Minutes(walkMetres)}"
                : DistanceText(walkMetres);

        static string Minutes(float metres) => $"{Mathf.Max(1, Mathf.RoundToInt(metres / WalkMetresPerMinute))} min";

        static string Metres(float metres) => metres >= 1000f
            ? (metres / 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " km"
            : (Mathf.Round(metres / 10f) * 10f).ToString("0", CultureInfo.InvariantCulture) + " m";
    }
}
