using System;

namespace AutoKey
{
    public static class Humanizer
    {
        private static readonly Random _rng = new();
        private static readonly object _lock = new();
        private static double _spareGaussian;
        private static bool _hasSpareGaussian;
        private static int _lastDelay;
        private static int _lastPressDuration;

        public static int AntiPatternLevel { get; set; } = 2;

        public static int NextDelay(int baseDelay, int randomRange)
        {
            lock (_lock)
            {
                int jitter = 0;
                if (randomRange > 0)
                    jitter = (int)(NextGaussian() * randomRange * 0.4);

                int delay = baseDelay + jitter;

                if (AntiPatternLevel >= 1)
                {
                    if (delay == _lastDelay)
                        delay += _rng.Next(1, 15) * (_rng.Next(0, 2) == 0 ? 1 : -1);
                }

                if (AntiPatternLevel >= 2 && _rng.Next(0, 100) < 8)
                    delay += _rng.Next(30, 120);

                delay = Math.Max(30, delay);
                _lastDelay = delay;
                return delay;
            }
        }

        public static int NextPressDuration()
        {
            lock (_lock)
            {
                int duration = 30 + (int)(Math.Abs(NextGaussian()) * 25);
                duration = Math.Clamp(duration, 20, 180);

                if (AntiPatternLevel >= 1 && duration == _lastPressDuration)
                    duration += _rng.Next(1, 10) * (_rng.Next(0, 2) == 0 ? 1 : -1);

                duration = Math.Clamp(duration, 20, 180);
                _lastPressDuration = duration;
                return duration;
            }
        }

        public static IntPtr NextExtraInfo()
        {
            lock (_lock)
            {
                return (IntPtr)_rng.Next(0, 256);
            }
        }

        public static int NextMicroJitter()
        {
            lock (_lock)
            {
                return _rng.Next(1, 8);
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
