namespace Bandroom.Api.Data;

/// <summary>
/// The band the current request is acting in, if any. Null means "no band
/// context" and the query filters then match NOTHING — fail closed. Legitimate
/// cross-band reads (my memberships, invite acceptance) opt out explicitly with
/// IgnoreQueryFilters plus their own where-clause.
/// </summary>
public interface IBandContext
{
    Guid? BandId { get; }
}
