using System;
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
    }

    /// Where true north lies in the headset's world. The Quest has no compass, so each phone reading gives one guess:
    /// the head's yaw when the reading was taken minus the phone's heading, which holds while the phone points the way
    /// the person faces. The guesses are averaged as unit vectors with exponential forgetting, so a bad one is diluted
    /// and a drifting world settles back.
    public sealed class NorthCalibration
    {
        /// Below this certainty the arrow stays hidden rather than point a wrong way.
        public const float ReadyStrength = 0.3f;

        float x, y;

        /// From 0 (no guesses) towards 1 (many agreeing guesses).
        public float Strength { get; private set; }

        public bool Ready => Strength >= ReadyStrength;

        /// The Unity yaw that points to true north.
        public float NorthYaw => Mathf.Atan2(x, y) * Mathf.Rad2Deg;

        /// Adds a guess of NorthYaw with `weight` between 0 and 1. The first guess is taken whole.
        public void Add(float northYaw, float weight)
        {
            weight = Mathf.Clamp01(weight);
            if (weight <= 0f) return;
            var r = northYaw * Mathf.Deg2Rad;
            var a = Strength <= 0f ? 1f : weight;
            x += a * (Mathf.Sin(r) - x);
            y += a * (Mathf.Cos(r) - y);
            Strength += weight * (1f - Strength);
        }

        public void Reset()
        {
            x = y = 0f;
            Strength = 0f;
        }
    }

    public static class NavMath
    {
        /// Walking pace, for the minutes shown under the arrow.
        public const float WalkMetresPerMinute = 80f;
        /// A GPS course is trusted only at walking pace or faster; standing still it points anywhere.
        public const float CourseMinSpeed = 0.8f;
        /// A compass that reports a worse error than this is ignored.
        public const float WorstCompassDegrees = 40f;
        public const float CourseWeight = 0.35f, CompassWeight = 0.15f;

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

        /// How much a reading counts towards north: a moving course most, a confident compass less, a poor one not.
        public static float CompassWeightFor(float headingAccuracy) =>
            headingAccuracy > WorstCompassDegrees ? 0f : CompassWeight;

        /// "240 m · 3 min walk", "1.4 km · 18 min walk".
        public static string DistanceText(float metres)
        {
            var minutes = Mathf.Max(1, Mathf.RoundToInt(metres / WalkMetresPerMinute));
            var distance = metres >= 1000f
                ? (metres / 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " km"
                : (Mathf.Round(metres / 10f) * 10f).ToString("0", CultureInfo.InvariantCulture) + " m";
            return $"{distance} · {minutes} min walk";
        }
    }
}
