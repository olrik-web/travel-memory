namespace TravelMemory.IntegrationTests.Integration;

[CollectionDefinition(Name)]
public sealed class ContainerTestCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "Container-backed tests";
}
