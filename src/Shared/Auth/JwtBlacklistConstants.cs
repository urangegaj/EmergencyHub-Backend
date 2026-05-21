namespace Shared.Auth;

public static class JwtBlacklistConstants
{
    public const string RevokedValue = "revoked";
    public const string KeyPrefix = "blacklist:jti:";

    public static string KeyForJti(string jti) => $"{KeyPrefix}{jti}";
}
