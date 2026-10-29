using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using LNU_Web.Models;
using LNU_Web.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LNU_Web.Services
{
    public class TokenService
    {
        private readonly IConfiguration _config;
        private readonly AdminOptions _admin;

        public TokenService(IConfiguration config, IOptions<AdminOptions> admin)
        {
            _config = config;
            _admin = admin.Value;
        }

        public string CreateToken(User user)
        {
            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email)
            };

            if (_admin.Emails.Any(email => string.Equals(email.Trim(), user.Email, StringComparison.OrdinalIgnoreCase)))
            {
                claims.Add(new Claim("role", AdminOptions.RoleName));
            }

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(double.Parse(_config["Jwt:ExpiresMinutes"]!)),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}