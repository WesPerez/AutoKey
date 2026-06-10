using System;

namespace AutoKey
{
    public static class Humanizer
    {
        private static readonly Random _rng = new();
        private static readonly object _lock = new();
        private static double _spareGaussian;
        private static bool _hasSpareGaussian;

        // Anti-repeat: track recent values to avoid patterns
        private static int _lastDelay;
        private static int _lastPressDuration;
        private static int _consecutiveCount;

        // Micro-pause: fatigue model — probability rises with keystroke count, resets after pause
        private static int _keystrokesSincePause;
        private const int FatigueThreshold = 12;

        public static int AntiPatternLevel { get; set; } = 2;

        public static int NextDelay(int baseDelay, int randomRange)
        {
            lock (_lock)
            {
                int delay;

                if (randomRange > 0)
                {
                    // Core jitter: Gaussian centered on baseDelay
                    int jitter = (int)(NextGaussian() * randomRange * 0.4);
                    delay = baseDelay + jitter;
                }
                else
                {
                    delay = baseDelay;
                }

                if (AntiPatternLevel >= 1)
                {
                    // Anti-repeat: avoid exact or near-exact repetition
                    if (delay == _lastDelay)
                    {
                        delay += _rng.Next(2, 20) * (_rng.Next(0, 2) == 0 ? 1 : -1);
                    }
                    else if (Math.Abs(delay - _lastDelay) < 3)
                    {
                        delay += _rng.Next(5, 15) * (delay > _lastDelay ? 1 : -1);
                    }

                    // Track consecutive similarity
                    if (Math.Abs(delay - _lastDelay) < 8)
                        _consecutiveCount++;
                    else
                        _consecutiveCount = 0;

                    // Break streaks: if too many similar delays in a row, force a larger deviation
                    if (_consecutiveCount >= 3)
                    {
                        delay += _rng.Next(15, 40) * (_rng.Next(0, 2) == 0 ? 1 : -1);
                        _consecutiveCount = 0;
                    }
                }

                if (AntiPatternLevel >= 2)
                {
                    // Fatigue-based micro-pause: probability grows with keystrokes since last pause
                    _keystrokesSincePause++;
                    double pauseProbability = Math.Min(0.25, _keystrokesSincePause / (double)FatigueThreshold * 0.08);

                    if (_rng.NextDouble() < pauseProbability)
                    {
                        // Pause duration scales with fatigue level
                        int fatigueFactor = Math.Min(_keystrokesSincePause / 5, 4);
                        delay += _rng.Next(40, 80 + fatigueFactor * 30);
                        _keystrokesSincePause = 0;
                    }
                }

                delay = Math.Max(20, delay);
                _lastDelay = delay;
                return delay;
            }
        }

        public static int NextPressDuration()
        {
            lock (_lock)
            {
                int duration;

                // Bimodal distribution: fast tap (85%) vs deliberate press (15%)
                if (_rng.NextDouble() < 0.85)
                {
                    // Fast tap: 35-65ms, peaked around 45ms
                    duration = 35 + (int)(Math.Abs(NextGaussian()) * 12);
                }
                else
                {
                    // Deliberate press: 80-160ms, peaked around 110ms
                    duration = 80 + (int)(Math.Abs(NextGaussian()) * 30);
                }

                duration = Math.Clamp(duration, 20, 200);

                if (AntiPatternLevel >= 1)
                {
                    // Anti-repeat for press duration
                    if (duration == _lastPressDuration)
                        duration += _rng.Next(2, 8) * (_rng.Next(0, 2) == 0 ? 1 : -1);

                    duration = Math.Clamp(duration, 20, 200);
                }

                _lastPressDuration = duration;
                return duration;
            }
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
