/// <summary>
/// The frame clock the game's loop reads (Three's Clock): seconds between calls, from <see cref="JS.Now"/>, so the smoke test's
/// fixed step (TestClock) drives it too.
/// </summary>
public class GameClock
{
    public bool autoStart = true;
    public double startTime, oldTime, elapsedTime;
    public bool running;

    public GameClock(bool autoStart = true) { this.autoStart = autoStart; }

    public void start()
    {
        startTime = JS.Now();
        oldTime = startTime;
        elapsedTime = 0;
        running = true;
    }

    public void stop() { getElapsedTime(); running = false; autoStart = false; }

    public double getElapsedTime() { getDelta(); return elapsedTime; }

    public double getDelta()
    {
        double diff = 0;
        if (autoStart && !running) { start(); return 0; }
        if (running)
        {
            double now = JS.Now();
            diff = (now - oldTime) / 1000;
            oldTime = now;
            elapsedTime += diff;
        }
        return diff;
    }
}
