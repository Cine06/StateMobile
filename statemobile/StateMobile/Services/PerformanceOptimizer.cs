using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace StateMobile.Services
{

    public static class PerformanceOptimizer
    {
   
        public static void RunBackgroundWorkAsync(Func<Task> work, string operationName = "Background Work")
        {
            Task.Run(async () =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    await work();
                    sw.Stop();
                    System.Diagnostics.Debug.WriteLine($"✅ [Perf] {operationName} completed in {sw.ElapsedMilliseconds}ms");
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    System.Diagnostics.Debug.WriteLine($"❌ [Perf] {operationName} failed after {sw.ElapsedMilliseconds}ms: {ex.Message}");
                }
            });
        }

       
        public static async Task<T> RunBackgroundWorkAsync<T>(Func<Task<T>> work, string operationName = "Background Work")
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var result = await Task.Run(async () => await work());
                sw.Stop();
                System.Diagnostics.Debug.WriteLine($"✅ [Perf] {operationName} completed in {sw.ElapsedMilliseconds}ms");
                return result;
            }
            catch (Exception ex)
            {
                sw.Stop();
                System.Diagnostics.Debug.WriteLine($"❌ [Perf] {operationName} failed after {sw.ElapsedMilliseconds}ms: {ex.Message}");
                throw;
            }
        }

       
        public static void ScheduleUIWorkAfterFrame(Func<Task> work, string operationName = "UI Work", int delayMs = 0)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs);
                }

                var sw = Stopwatch.StartNew();
                try
                {
                    await work();
                    sw.Stop();
                    System.Diagnostics.Debug.WriteLine($"✅ [Perf] {operationName} completed in {sw.ElapsedMilliseconds}ms");
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    System.Diagnostics.Debug.WriteLine($"❌ [Perf] {operationName} failed after {sw.ElapsedMilliseconds}ms: {ex.Message}");
                }
            });
        }

        [Conditional("DEBUG")]
        public static void MeasureOperation(Action operation, string operationName)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                operation();
                sw.Stop();
                if (sw.ElapsedMilliseconds > 16)  
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ [Perf] {operationName} took {sw.ElapsedMilliseconds}ms (>16ms frame time)");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"✅ [Perf] {operationName} completed in {sw.ElapsedMilliseconds}ms");
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                System.Diagnostics.Debug.WriteLine($"❌ [Perf] {operationName} failed after {sw.ElapsedMilliseconds}ms: {ex.Message}");
            }
        }

        
        private static readonly Dictionary<string, DateTime> _lastExecutionTimes = new();

        public static bool ShouldExecuteDebounced(string operationId, int minIntervalMs = 500)
        {
            lock (_lastExecutionTimes)
            {
                if (!_lastExecutionTimes.TryGetValue(operationId, out var lastTime))
                {
                    _lastExecutionTimes[operationId] = DateTime.UtcNow;
                    return true;
                }

                var timeSinceLastExecution = (DateTime.UtcNow - lastTime).TotalMilliseconds;
                if (timeSinceLastExecution >= minIntervalMs)
                {
                    _lastExecutionTimes[operationId] = DateTime.UtcNow;
                    return true;
                }

                return false;
            }
        }

 
        public static async Task<T> ThrottleAsync<T>(
            string operationId,
            Func<Task<T>> operation,
            int minIntervalMs = 500)
        {
            if (!ShouldExecuteDebounced(operationId, minIntervalMs))
            {
                System.Diagnostics.Debug.WriteLine($"⏭️ [Perf] {operationId} throttled - running too frequently");
                return default!;
            }

            return await operation();
        }
    }
}
