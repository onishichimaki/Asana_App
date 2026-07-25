using System.Security.Cryptography;
using System.Text;

namespace TaskCapture.Api.Security;

public static class UserIdentityKey
{
    public static string Create(string provider, string subject)
    {
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes($"{provider.Trim()}|{subject.Trim().ToLowerInvariant()}"));
        return $"auth-{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }
}
