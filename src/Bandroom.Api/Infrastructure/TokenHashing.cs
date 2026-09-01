using System.Security.Cryptography;
using System.Text;

namespace Bandroom.Api.Infrastructure;

public static class TokenHashing
{
    /// <summary>Opaque credentials (refresh + invite tokens) are stored only as this hash.</summary>
    public static string Sha256Hex(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
