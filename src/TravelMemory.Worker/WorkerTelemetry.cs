using System.Diagnostics;

namespace TravelMemory.Worker;

internal static class WorkerTelemetry
{
    // Named after the application, which ServiceDefaults registers as a tracing source.
    public static readonly ActivitySource ActivitySource = new("TravelMemory.Worker");
}
