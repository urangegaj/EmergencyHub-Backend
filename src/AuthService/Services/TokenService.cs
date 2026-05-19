using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using AuthService.Models;
using Microsoft.IdentityModel.Tokens;
using Shared.Auth;

namespace AuthService.Services;

public class TokenService
{
    private readonly RsaSecurityKey _privateKey;
    private readonly RsaSecurityKey _publicKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _expiryMinutes;

    public TokenService(IConfiguration config)
    {
        var privateRsa = RSA.Create();
        privateRsa.ImportFromPem(
            System.Text.Encoding.UTF8.GetString(
                Convert.FromBase64String(config["Jwt:PrivateKeyBase64"]!)));

        var publicRsa = RSA.Create();
        publicRsa.ImportFromPem(
            System.Text.Encoding.UTF8.GetString(
                Convert.FromBase64String(config["Jwt:PublicKeyBase64"]!)));

        _privateKey = new RsaSecurityKey(privateRsa);
        _publicKey = new RsaSecurityKey(publicRsa);
        _issuer = config["Jwt:Issuer"]!;
        _audience = config["Jwt:Audience"]!;
        _expiryMinutes = int.Parse(config["Jwt:AccessTokenExpiryMinutes"]!);
    }

    public string IssueAccessToken(User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimNames.UserId, user.Id.ToString()),
            new(ClaimNames.CityId, user.CityId.ToString()),
            new(ClaimNames.Role, user.Role.Name)
        };

        if (user.Department.HasValue)
            claims.Add(new Claim(ClaimNames.Department, user.Department.Value.ToString()));

        var credentials = new SigningCredentials(_privateKey, SecurityAlgorithms.RsaSha256);

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_expiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public JsonWebKeySet GetJwks()
    {
        var key = JsonWebKeyConverter.ConvertFromRSASecurityKey(_publicKey);
        key.Use = "sig";
        key.Alg = SecurityAlgorithms.RsaSha256;
        return new JsonWebKeySet { Keys = { key } };
    }
}
