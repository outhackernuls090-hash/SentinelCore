using System.Security.Cryptography;
using System.Text;

namespace SentinelServer;

public static class ApiKeys
{
    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return "sk_" + token;
    }

    public static string Hash(string apiKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));
}

public static class RoleCheck
{
    private static int Rank(string role) => role switch
    {
        "Owner" => 4,
        "Administrator" => 3,
        "Analyst" => 2,
        "Viewer" => 1,
        _ => 0
    };

    public static bool Satisfies(string userRole, string requiredRole) => Rank(userRole) >= Rank(requiredRole);

    public static bool IsValidRole(string role) => role is "Viewer" or "Analyst" or "Administrator" or "Owner";
}
