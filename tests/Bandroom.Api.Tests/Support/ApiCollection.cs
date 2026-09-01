using Xunit;

namespace Bandroom.Api.Tests.Support;

/// <summary>
/// One shared API host + one Postgres container for every integration-test
/// class. Tests stay independent by always creating fresh users and bands.
/// </summary>
[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>;
