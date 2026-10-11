namespace Quotebook.Web.Infrastructure;

/// <summary>
/// "Today" for the business means Malaysia's today (UTC+8, no daylight saving), wherever the
/// server runs. A fixed offset keeps it identical on Windows and Linux hosts.
/// </summary>
public sealed class MalaysiaTimeProvider : TimeProvider
{
    static readonly TimeZoneInfo Zone =
        TimeZoneInfo.CreateCustomTimeZone("MYT", TimeSpan.FromHours(8), "Malaysia Time", "Malaysia Time");

    public override TimeZoneInfo LocalTimeZone => Zone;

    public DateOnly Today => DateOnly.FromDateTime(GetLocalNow().DateTime);
}
