using System;
using System.Collections.Generic;

namespace AutoKey
{
    public static class Humanizer
    {
        private sealed class DelayState
        {
            public int LastDelay { get; set; }
            public int ConsecutiveCount { get; set; }
            public int KeystrokesSincePause { get; set; }
        }

        private static readonly Random _rng = new();
        private static readonly object _lock = new();
        private static readonly Dictionary<int, DelayState> _delayStates = new();
        private static double _spareGaussian;
        private static bool _hasSpareGaussian;
        private static int _lastPressDuration;

        private const int DefaultProfileId = 0;
        private const int MinimumDelay = 20;
        private const int FatigueThreshold = 12;

        public static int AntiPatternLevel { get; set; } = 2;

        public static void Reset()
        {
            lock (_lock)
            {
                _delayStates.Clear();
                _lastPressDuration = 0;
                _hasSpareGaussian = false;
                _spareGaussian = 0;
            }
        }

        public static int NextDelay(int baseDelay, int randomRange)
            => NextDelay(baseDelay, randomRange, DefaultProfileId);

        public static int NextDelay(int baseDelay, int randomRange, int profileId)
        {
            lock (_lock)
            {
                baseDelay = Math.Max(0, baseDelay);
                randomRange = Math.Max(0, randomRange);

                var state = GetDelayState(profileId);
                int minDelay = GetMinimumDelay(baseDelay);
                int maxDelay = randomRange > 0
                    ? (int)Math.Min(int.MaxValue - 1L, (long)baseDelay + randomRange)
                    : Math.Max(minDelay, baseDelay);

                int delay = baseDelay;
                if (randomRange > 0)
                {
                    double sigma = Math.Max(1.0, randomRange / 3.0);
                    int jitter = (int)Math.Round(NextGaussian() * sigma);
                    jitter = Math.Clamp(jitter, minDelay - baseDelay, randomRange);
                    delay = baseDelay + jitter;
                }

                delay = Math.Clamp(delay, minDelay, maxDelay);

                if (AntiPatternLevel >= 1)
                {
                    delay = ApplyAntiRepeat(delay, state, minDelay, maxDelay);
                }

                if (AntiPatternLevel >= 2)
                {
                    state.KeystrokesSincePause++;
                    double pauseProbability = Math.Min(0.18, state.KeystrokesSincePause / (double)FatigueThreshold * 0.06);

                    if (_rng.NextDouble() < pauseProbability)
                    {
                        int fatigueFactor = Math.Min(state.KeystrokesSincePause / 5, 4);
                        delay = (int)Math.Min(int.MaxValue - 1L, (long)delay + _rng.Next(35, 75 + fatigueFactor * 25));
                        state.KeystrokesSincePause = 0;
                    }
                }

                state.LastDelay = delay;
                return delay;
            }
        }

        public static int NextPressDuration()
        {
            lock (_lock)
            {
                if (AntiPatternLevel <= 0)
                    return 45;

                int duration = _rng.NextDouble() < 0.85
                    ? 35 + (int)(Math.Abs(NextGaussian()) * 12)
                    : 80 + (int)(Math.Abs(NextGaussian()) * 30);

                duration = Math.Clamp(duration, 20, 200);

                if (AntiPatternLevel >= 1 && duration == _lastPressDuration)
                {
                    duration += _rng.Next(2, 8) * (_rng.Next(0, 2) == 0 ? 1 : -1);
                    duration = Math.Clamp(duration, 20, 200);
                }

                _lastPressDuration = duration;
                return duration;
            }
        }

        private static DelayState GetDelayState(int profileId)
        {
            if (!_delayStates.TryGetValue(profileId, out var state))
            {
                state = new DelayState();
                _delayStates[profileId] = state;
            }

            return state;
        }

        private static int GetMinimumDelay(int baseDelay)
        {
            if (baseDelay <= MinimumDelay)
                return MinimumDelay;

            if (baseDelay < 1000)
                return Math.Max(MinimumDelay, baseDelay / 2);

            return MinimumDelay;
        }

        private static int ApplyAntiRepeat(int delay, DelayState state, int minDelay, int maxDelay)
        {
            if (state.LastDelay <= 0 || minDelay >= maxDelay)
                return delay;

            int delta = Math.Abs(delay - state.LastDelay);
            if (delta <= 2)
            {
                delay = NudgeAway(delay, state.LastDelay, minDelay, maxDelay, 2, 18);
            }

            if (Math.Abs(delay - state.LastDelay) < 8)
                state.ConsecutiveCount++;
            else
                state.ConsecutiveCount = 0;

            if (state.ConsecutiveCount >= 3)
            {
                delay = NudgeAway(delay, state.LastDelay, minDelay, maxDelay, 15, 40);
                state.ConsecutiveCount = 0;
            }

            return delay;
        }

        private static int NudgeAway(int delay, int lastDelay, int minDelay, int maxDelay, int minStep, int maxStep)
        {
            int step = _rng.Next(minStep, maxStep + 1);
            int upRoom = maxDelay - delay;
            int downRoom = delay - minDelay;

            bool preferUp = delay >= lastDelay;
            if (preferUp && upRoom > 0)
                return delay + Math.Min(step, upRoom);

            if (!preferUp && downRoom > 0)
                return delay - Math.Min(step, downRoom);

            if (upRoom >= downRoom && upRoom > 0)
                return delay + Math.Min(step, upRoom);

            if (downRoom > 0)
                return delay - Math.Min(step, downRoom);

            return delay;
        }

        private static double NextGaussian()
        {
            if (_hasSpareGaussian)
            {
                _hasSpareGaussian = false;
                return _spareGaussian;
            }

            double u, v, s;
            do
            {
                u = _rng.NextDouble() * 2.0 - 1.0;
                v = _rng.NextDouble() * 2.0 - 1.0;
                s = u * u + v * v;
            } while (s >= 1.0 || s == 0.0);

            double factor = Math.Sqrt(-2.0 * Math.Log(s) / s);
            _spareGaussian = v * factor;
            _hasSpareGaussian = true;
            return u * factor;
        }
    }
}
