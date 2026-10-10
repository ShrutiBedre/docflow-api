using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DocFlow.Api.Common;
using DocFlow.Api.Data;
using DocFlow.Api.Domain;
using DocFlow.Api.DTOs;
using DocFlow.Api.Repositories;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DocFlow.Api.Services;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
}

public class AuthService(
    IUserRepository users,
    IUnitOfWork uow,
    IOptions<JwtSettings> jwtOptions,
    TimeProvider clock) : IAuthService
{
    private readonly JwtSettings _jwt = jwtOptions.Value;

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        // Admins are never self-registered; they would be created by seeding or another admin.
        if (request.Role == UserRole.Admin)
            throw new BadRequestException("Admin accounts cannot be self-registered.");

        var email = request.Email.Trim().ToLowerInvariant();
        if (await users.EmailExistsAsync(email, ct))
            throw new BusinessRuleException("An account with this email already exists.");

        var user = new User
        {
            Email = email,
            FullName = request.FullName.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = request.Role,
            CreatedAt = clock.GetUtcNow().UtcDateTime
        };

        await users.AddAsync(user, ct);
        await uow.SaveChangesAsync(ct);

        return CreateToken(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await users.GetByEmailAsync(request.Email.Trim().ToLowerInvariant(), ct);

        // Same message for "no such user" and "wrong password" so attackers can't enumerate emails.
        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw new BadRequestException("Invalid email or password.");

        return CreateToken(user);
    }

    private AuthResponse CreateToken(User user)
    {
        var expires = clock.GetUtcNow().UtcDateTime.AddMinutes(_jwt.ExpiryMinutes);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.FullName),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Key));
        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new AuthResponse(new JwtSecurityTokenHandler().WriteToken(token), expires, user.ToDto());
    }
}
