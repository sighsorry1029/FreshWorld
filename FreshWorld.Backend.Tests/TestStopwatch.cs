using RealStopwatch = System.Diagnostics.Stopwatch;

namespace FreshWorld.Backend;

// Test-only clock boundary for the source-linked backend. The plugin still uses the BCL
// Stopwatch. Existing tests use real time; long loading tests explicitly opt into manual time.
internal sealed class Stopwatch
{
    private static ManualTime? activeTime;
    private readonly ManualTime? time = activeTime;
    private readonly RealStopwatch real = new();
    private long elapsed;
    private long started;
    private bool running;

    public static long Frequency => RealStopwatch.Frequency;
    public long ElapsedTicks => time == null ? real.ElapsedTicks : elapsed + (running ? time.Ticks - started : 0);
    public TimeSpan Elapsed => TimeSpan.FromSeconds((double)ElapsedTicks / Frequency);
    public long ElapsedMilliseconds => (long)Elapsed.TotalMilliseconds;

    public static Stopwatch StartNew() { var timer = new Stopwatch(); timer.Start(); return timer; }
    public void Start()
    {
        if (time == null) { real.Start(); return; }
        if (running) return;
        started = time.Ticks;
        running = true;
    }
    public void Stop()
    {
        if (time == null) { real.Stop(); return; }
        elapsed = ElapsedTicks;
        running = false;
    }
    public void Restart()
    {
        if (time == null) { real.Restart(); return; }
        elapsed = 0;
        started = time.Ticks;
        running = true;
    }

    public static ManualTime UseManualTime()
    {
        if (activeTime != null) throw new InvalidOperationException("Manual time is already active.");
        return activeTime = new ManualTime();
    }

    internal sealed class ManualTime : IDisposable
    {
        public long Ticks { get; private set; }
        public void Advance(TimeSpan duration)
        {
            if (!ReferenceEquals(activeTime, this)) throw new InvalidOperationException("Manual time is not active.");
            if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
            Ticks = checked(Ticks + (long)(duration.TotalSeconds * Frequency));
        }
        public void Dispose() { if (ReferenceEquals(activeTime, this)) activeTime = null; }
    }
}
