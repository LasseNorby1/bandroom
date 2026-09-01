using Microsoft.EntityFrameworkCore;

namespace Bandroom.Api.Data;

/// <summary>
/// Entities land with steps 2–3 of the build order (identity, bands + memberships)
/// so the first migration ships one coherent schema. The Npgsql + NodaTime wiring
/// is proven here from day one.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);
