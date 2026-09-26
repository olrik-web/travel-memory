using System.Diagnostics;

namespace TravelMemory.Persistence.Photos;

public static class PhotoJobTracing
{
    // The trace context to store on a new job: the current span, if it has a W3C id.
    public static string? CurrentTraceParent =>
        Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity ? activity.Id : null;
}
