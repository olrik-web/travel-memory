namespace TravelMemory.Api.Tests.Integration;

// Every integration test starts its own SQL Server container. Starting several at once
// on a small CI runner can crash SQL Server during startup, so these tests run one at a time.
[CollectionDefinition(Name)]
public sealed class ContainerTestCollection
{
    public const string Name = "Container-backed tests";
}
