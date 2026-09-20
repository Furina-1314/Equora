namespace Equora.App.Services;

public sealed class RestrictionPause
{
    public DateTimeOffset? Until { get; private set; }
    public void Start(DateTimeOffset now) => Until = now.AddMinutes(15);
    public TimeSpan Remaining(DateTimeOffset now) => Until is { } until
        ? TimeSpan.FromSeconds(Math.Max(0, Math.Ceiling((until - now).TotalSeconds))) : TimeSpan.Zero;
    public bool TryExpire(DateTimeOffset now)
    {
        if (Until is not { } until || now < until) return false;
        Until = null;
        return true;
    }
}
