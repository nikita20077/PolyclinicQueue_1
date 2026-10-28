using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using PolyclinicQueue.API.Models;

namespace PolyclinicQueue.API.Services;

public class JwtService
{
    private readonly IConfiguration _cfg;
    public JwtService(IConfiguration cfg) => _cfg = cfg;

    public string CreateToken(User user, string role, int profileId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_cfg["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Login),
            new Claim(ClaimTypes.Role, role),
            new Claim("profileId", profileId.ToString())
        };
        var hours = double.TryParse(_cfg["Jwt:ExpireHours"], System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var h) ? h : 8;
        var token = new JwtSecurityToken(_cfg["Jwt:Issuer"], _cfg["Jwt:Audience"], claims,
            expires: DateTime.UtcNow.AddHours(hours), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
