using System.Diagnostics;

namespace StateMobile.Services
{
    public static class StartupTimingLogger
    {
        private static readonly object _sync = new();
        private static readonly Dictionary<string, long> _marks = new();
        private static long _sessionStartMs = Environment.TickCount64;
        private static long _lastMarkMs;
        private static bool _summaryPrinted;

        public static void Reset(string reason = "")
        {
            lock (_sync)
            {
                _sessionStartMs = Environment.TickCount64;
                _lastMarkMs = 0;
                _summaryPrinted = false;
                _marks.Clear();
                Debug.WriteLine($"⏱️ [StartupTiming] Reset {(string.IsNullOrWhiteSpace(reason) ? string.Empty : $"({reason})")}");
            }
        }

        public static void Mark(string phase)
        {
            var nowMs = ElapsedSinceSessionStartMs();

            lock (_sync)
            {
                var deltaMs = nowMs - _lastMarkMs;
                _lastMarkMs = nowMs;
                _marks[phase] = nowMs;
                Debug.WriteLine($"⏱️ [StartupTiming] {phase}: +{deltaMs}ms (t={nowMs}ms)");
            }
        }

        public static void PrintSummaryIfAvailable()
        {
            lock (_sync)
            {
                if (_summaryPrinted)
                {
                    return;
                }

                if (!_marks.ContainsKey("login.tap") || !_marks.ContainsKey("auth.response") || !_marks.ContainsKey("home.rendered"))
                {
                    return;
                }

                var tapToAuthMs = _marks["auth.response"] - _marks["login.tap"];
                var authToHomeMs = _marks["home.rendered"] - _marks["auth.response"];
                var tapToHomeMs = _marks["home.rendered"] - _marks["login.tap"];

                Debug.WriteLine($"⏱️ [StartupTiming][Summary] login.tap -> auth.response: {tapToAuthMs}ms");
                Debug.WriteLine($"⏱️ [StartupTiming][Summary] auth.response -> home.rendered: {authToHomeMs}ms");
                Debug.WriteLine($"⏱️ [StartupTiming][Summary] login.tap -> home.rendered: {tapToHomeMs}ms");

                _summaryPrinted = true;
            }
        }

        private static long ElapsedSinceSessionStartMs()
        {
            return Environment.TickCount64 - _sessionStartMs;
        }
    }
}
