using Microsoft.AspNetCore.Identity;

namespace Bandroom.Api.Data;

public sealed class AppUser : IdentityUser<Guid>
{
    // UUIDv7 keys everywhere — sortable, index-friendly (foundation rules, spec §6).
    public AppUser() => Id = Guid.CreateVersion7();

    public required string DisplayName { get; set; }
}
