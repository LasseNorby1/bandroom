namespace Bandroom.Api.Data;

/// <summary>
/// Marker for every entity that belongs to one band. Each implementer MUST get a
/// global query filter in <see cref="AppDbContext.OnModelCreating"/> — a test
/// walks the model and fails if one is missing, so forgetting is a build break,
/// not a data leak.
/// </summary>
public interface IBandScoped
{
    Guid BandId { get; }
}
