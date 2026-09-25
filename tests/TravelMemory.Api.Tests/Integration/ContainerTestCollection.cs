namespace TravelMemory.Api.Tests.Integration;

[CollectionDefinition(Name)]
public sealed class ContainerTestCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "Container-backed tests";
}
