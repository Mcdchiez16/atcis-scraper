using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ZimbabweTenderAPI.Data;
using ZimbabweTenderAPI.Data.Entities;
using ZimbabweTenderAPI.DTOs;
using Microsoft.Extensions.Logging;
// NOTE: IHttpContextAccessor and System.Net.Http are NOT needed here.

namespace ZimbabweTenderAPI.Services
{
    public interface IAuthService
    {
        Task<AuthResponse> RegisterAsync(RegisterRequest request, string? ipAddress = null, string? userAgent = null);
        Task<AuthResponse> LoginAsync(LoginRequest request, string? ipAddress = null, string? userAgent = null);
        Task<AuthResponse> RefreshTokenAsync(string refreshToken, string? ipAddress = null, string? userAgent = null);
        Task<bool> RevokeTokenAsync(string refreshToken, string? ipAddress = null, string? userAgent = null);
        Task<UserDto> GetCurrentUserAsync(string username);
        Task<AuthResponse> ChangePasswordAsync(string username, string oldPassword, string newPassword, string? ipAddress = null, string? userAgent = null);
        Task<List<UserDto>> GetAllUsersAsync();
        Task<List<string>?> GetUserRolesAsync(int userId);
        Task<AuthResponse> AssignRoleAsync(int userId, string role, string adminUsername);
        Task<AuthResponse> RemoveRoleAsync(int userId, string role, string adminUsername);
    }

    public class AuthService : IAuthService
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthService> _logger;
        // IHttpContextAccessor removed

        public AuthService(
            ApplicationDbContext context,
            IConfiguration configuration,
            ILogger<AuthService> logger)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<AuthResponse> RegisterAsync(RegisterRequest request, string? ipAddress = null, string? userAgent = null)
        {
            try
            {
                if (await _context.Users.AnyAsync(u => u.Username == request.Username))
                {
                    return new AuthResponse { Success = false, Message = "Username already exists" };
                }

                if (await _context.Users.AnyAsync(u => u.Email == request.Email))
                {
                    return new AuthResponse { Success = false, Message = "Email already exists" };
                }

                var user = new UserEntity
                {
                    Username = request.Username,
                    Email = request.Email,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    IsActive = true,
                    EmailConfirmed = false,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = request.Username
                };

                await _context.Users.AddAsync(user);

                // ✅ SAVE FIRST to generate user.Id
                await _context.SaveChangesAsync();

                // Assign roles to user
                var rolesToAssign = request.Roles != null && request.Roles.Any() 
                    ? request.Roles 
                    : new List<string> { "Employee" }; // Default role

                foreach (var roleName in rolesToAssign)
                {
                    var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == roleName);
                    if (role != null)
                    {
                        var userRole = new UserRoleEntity
                        {
                            UserId = user.Id,
                            RoleId = role.Id,
                            AssignedAt = DateTime.UtcNow,
                            AssignedBy = request.Username
                        };
                        await _context.UserRoles.AddAsync(userRole);
                    }
                }

                await _context.SaveChangesAsync();

                // ✅ NOW create audit log with valid user.Id
                await AddUserAuditLog(user.Id, "Register", "User registered successfully", ipAddress, userAgent, user.Username);

                var accessToken = GenerateJwtToken(user);
                var refreshToken = GenerateRefreshToken();

                user.RefreshToken = refreshToken;
                user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);

                // ✅ Save again for refresh token
                await _context.SaveChangesAsync();

                return new AuthResponse
                {
                    Success = true,
                    Message = "Registration successful",
                    AccessToken = accessToken,
                    RefreshToken = refreshToken,
                    User = await MapToUserDtoAsync(user.Id)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during user registration");
                return new AuthResponse { Success = false, Message = $"Registration failed: {ex.Message}" };
            }
        }
        public async Task<AuthResponse> LoginAsync(LoginRequest request, string? ipAddress = null, string? userAgent = null)
        {
            try
            {
                var user = await _context.Users
                    .Include(u => u.UserRoles)
                        .ThenInclude(ur => ur.Role)
                    .FirstOrDefaultAsync(u => u.Username == request.Username || u.Email == request.Username);

                if (user == null)
                {
                    return new AuthResponse { Success = false, Message = "Invalid username or password" };
                }

                if (user.LockoutEnd.HasValue && user.LockoutEnd > DateTime.UtcNow)
                {
                    return new AuthResponse
                    {
                        Success = false,
                        Message = $"Account is locked until {user.LockoutEnd.Value:yyyy-MM-dd HH:mm}"
                    };
                }

                if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                {
                    user.FailedLoginAttempts++;

                    if (user.FailedLoginAttempts >= 5)
                    {
                        user.LockoutEnd = DateTime.UtcNow.AddMinutes(30);
                        // FIX: Pass audit parameters and username
                        await AddUserAuditLog(user.Id, "AccountLocked", "Account locked due to failed login attempts", ipAddress, userAgent, user.Username);
                    }

                    await _context.SaveChangesAsync();

                    return new AuthResponse { Success = false, Message = "Invalid username or password" };
                }

                if (!user.IsActive)
                {
                    return new AuthResponse { Success = false, Message = "Account is deactivated" };
                }

                // Successful login updates
                user.FailedLoginAttempts = 0;
                user.LockoutEnd = null;
                user.LastLoginAt = DateTime.UtcNow;
                user.LastLoginIP = ipAddress; // Use the passed IP address

                var accessToken = GenerateJwtToken(user);
                var refreshToken = GenerateRefreshToken();

                user.RefreshToken = refreshToken;
                user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);

                // FIX: Pass audit parameters and username
                await AddUserAuditLog(user.Id, "Login", "User logged in successfully", ipAddress, userAgent, user.Username);
                await _context.SaveChangesAsync();

                return new AuthResponse
                {
                    Success = true,
                    Message = "Login successful",
                    AccessToken = accessToken,
                    RefreshToken = refreshToken,
                    User = await MapToUserDtoAsync(user.Id)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login");
                return new AuthResponse { Success = false, Message = $"Login failed: {ex.Message}" };
            }
        }

        public async Task<AuthResponse> RefreshTokenAsync(string refreshToken, string? ipAddress = null, string? userAgent = null)
        {
            try
            {
                var user = await _context.Users
                    .Include(u => u.UserRoles)
                        .ThenInclude(ur => ur.Role)
                    .FirstOrDefaultAsync(u => u.RefreshToken == refreshToken);

                if (user == null || user.RefreshTokenExpiryTime < DateTime.UtcNow)
                {
                    return new AuthResponse { Success = false, Message = "Invalid or expired refresh token" };
                }

                var accessToken = GenerateJwtToken(user);
                var newRefreshToken = GenerateRefreshToken();

                user.RefreshToken = newRefreshToken;
                user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);

                // FIX: Audit the refresh event
                await AddUserAuditLog(user.Id, "TokenRefresh", "Access token refreshed", ipAddress, userAgent, user.Username);

                await _context.SaveChangesAsync();

                return new AuthResponse
                {
                    Success = true,
                    Message = "Token refreshed successfully",
                    AccessToken = accessToken,
                    RefreshToken = newRefreshToken,
                    User = await MapToUserDtoAsync(user.Id)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing token");
                return new AuthResponse { Success = false, Message = $"Token refresh failed: {ex.Message}" };
            }
        }

        public async Task<bool> RevokeTokenAsync(string refreshToken, string? ipAddress = null, string? userAgent = null)
        {
            try
            {
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.RefreshToken == refreshToken);

                if (user == null)
                    return false;

                user.RefreshToken = null;
                user.RefreshTokenExpiryTime = null;

                // FIX: Pass audit parameters and username
                await AddUserAuditLog(user.Id, "Logout", "User logged out", ipAddress, userAgent, user.Username);
                await _context.SaveChangesAsync();

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error revoking token");
                return false;
            }
        }

        public async Task<UserDto> GetCurrentUserAsync(string username)
        {
            var user = await _context.Users
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.Username == username);

            return user != null ? await MapToUserDtoAsync(user.Id) : null;
        }

        public async Task<AuthResponse> ChangePasswordAsync(string username, string oldPassword, string newPassword, string? ipAddress = null, string? userAgent = null)
        {
            try
            {
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Username == username);

                if (user == null)
                    return new AuthResponse { Success = false, Message = "User not found" };

                if (!BCrypt.Net.BCrypt.Verify(oldPassword, user.PasswordHash))
                    return new AuthResponse { Success = false, Message = "Invalid old password" };

                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);

                // FIX: Pass audit parameters and username
                await AddUserAuditLog(user.Id, "PasswordChange", "Password changed successfully", ipAddress, userAgent, user.Username);

                // Security: Clear refresh token to force relogin after password change
                user.RefreshToken = null;
                user.RefreshTokenExpiryTime = null;

                await _context.SaveChangesAsync();

                return new AuthResponse { Success = true, Message = "Password changed successfully" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error changing password");
                return new AuthResponse { Success = false, Message = $"Password change failed: {ex.Message}" };
            }
        }

        // Helper methods

        // FIX: Removed GetClientIP and GetUserAgent methods entirely as they are handled in the controller.

        // FIX: Updated to accept optional audit parameters and ChangedBy field
        private async Task AddUserAuditLog(int userId, string action, string details, string? ipAddress = null, string? userAgent = null, string? changedBy = null)
        {
            var auditLog = new UserAuditLog
            {
                UserId = userId,
                Action = action,
                Details = details,
                ChangeDate = DateTime.UtcNow,
                IPAddress = ipAddress,
                UserAgent = userAgent,
                ChangedBy = changedBy ?? "System" // Use passed username or default to "System"
            };

            await _context.UserAuditLogs.AddAsync(auditLog);
            // Note: SaveChangesAsync relies on the caller
        }

        private string GenerateJwtToken(UserEntity user)
        {
            var jwtSettings = _configuration.GetSection("JwtSettings");
            var secretKey = jwtSettings["SecretKey"];
            var issuer = jwtSettings["Issuer"];
            var audience = jwtSettings["Audience"];

            if (!int.TryParse(jwtSettings["ExpirationMinutes"], out int expirationMinutes))
            {
                expirationMinutes = 60;
            }

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim("FirstName", user.FirstName ?? ""),
                new Claim("LastName", user.LastName ?? "")
            };

            // Add multiple role claims
            if (user.UserRoles != null)
            {
                foreach (var userRole in user.UserRoles)
                {
                    claims.Add(new Claim(ClaimTypes.Role, userRole.Role.Name));
                }
            }

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(expirationMinutes),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private string GenerateRefreshToken()
        {
            var randomNumber = new byte[64];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomNumber);
            return Convert.ToBase64String(randomNumber);
        }

        private async Task<UserDto> MapToUserDtoAsync(int userId)
        {
            var user = await _context.Users
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                return null;

            return new UserDto
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Roles = user.UserRoles?.Select(ur => ur.Role.Name).ToList() ?? new List<string>(),
                IsActive = user.IsActive,
                EmailConfirmed = user.EmailConfirmed,
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt
            };
        }

        // ==================== ROLE MANAGEMENT METHODS ====================

        public async Task<List<UserDto>> GetAllUsersAsync()
        {
            var users = await _context.Users
                .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                .ToListAsync();

            return users.Select(u => new UserDto
            {
                Id = u.Id,
                Username = u.Username,
                Email = u.Email,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Roles = u.UserRoles.Select(ur => ur.Role.Name).ToList(),
                IsActive = u.IsActive,
                EmailConfirmed = u.EmailConfirmed,
                CreatedAt = u.CreatedAt,
                LastLoginAt = u.LastLoginAt
            }).ToList();
        }

        public async Task<List<string>?> GetUserRolesAsync(int userId)
        {
            var user = await _context.Users
                .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                return null;

            return user.UserRoles.Select(ur => ur.Role.Name).ToList();
        }

        public async Task<AuthResponse> AssignRoleAsync(int userId, string roleName, string adminUsername)
        {
            try
            {
                var user = await _context.Users
                    .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null)
                {
                    return new AuthResponse
                    {
                        Success = false,
                        Message = "User not found"
                    };
                }

                var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == roleName);
                if (role == null)
                {
                    return new AuthResponse
                    {
                        Success = false,
                        Message = $"Role '{roleName}' not found"
                    };
                }

                // Check if user already has this role
                if (user.UserRoles.Any(ur => ur.RoleId == role.Id))
                {
                    return new AuthResponse
                    {
                        Success = false,
                        Message = $"User already has the '{roleName}' role"
                    };
                }

                // Add the role
                user.UserRoles.Add(new UserRoleEntity
                {
                    UserId = user.Id,
                    RoleId = role.Id,
                    AssignedAt = DateTime.UtcNow,
                    AssignedBy = adminUsername
                });

                user.UpdatedAt = DateTime.UtcNow;
                user.UpdatedBy = adminUsername;

                await _context.SaveChangesAsync();

                _logger.LogInformation($"Role '{roleName}' assigned to user '{user.Username}' by '{adminUsername}'");

                return new AuthResponse
                {
                    Success = true,
                    Message = $"Role '{roleName}' successfully assigned to user '{user.Username}'"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error assigning role to user {userId}");
                return new AuthResponse
                {
                    Success = false,
                    Message = $"Error assigning role: {ex.Message}"
                };
            }
        }

        public async Task<AuthResponse> RemoveRoleAsync(int userId, string roleName, string adminUsername)
        {
            try
            {
                var user = await _context.Users
                    .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null)
                {
                    return new AuthResponse
                    {
                        Success = false,
                        Message = "User not found"
                    };
                }

                var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == roleName);
                if (role == null)
                {
                    return new AuthResponse
                    {
                        Success = false,
                        Message = $"Role '{roleName}' not found"
                    };
                }

                var userRole = user.UserRoles.FirstOrDefault(ur => ur.RoleId == role.Id);
                if (userRole == null)
                {
                    return new AuthResponse
                    {
                        Success = false,
                        Message = $"User does not have the '{roleName}' role"
                    };
                }

                // Prevent removing all roles (user must have at least one role)
                if (user.UserRoles.Count == 1)
                {
                    return new AuthResponse
                    {
                        Success = false,
                        Message = "Cannot remove the last role. User must have at least one role."
                    };
                }

                // Remove the role
                user.UserRoles.Remove(userRole);
                user.UpdatedAt = DateTime.UtcNow;
                user.UpdatedBy = adminUsername;

                await _context.SaveChangesAsync();

                _logger.LogInformation($"Role '{roleName}' removed from user '{user.Username}' by '{adminUsername}'");

                return new AuthResponse
                {
                    Success = true,
                    Message = $"Role '{roleName}' successfully removed from user '{user.Username}'"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error removing role from user {userId}");
                return new AuthResponse
                {
                    Success = false,
                    Message = $"Error removing role: {ex.Message}"
                };
            }
        }
    }

    // DTOs for Authentication (Updated for multiple roles)
    public class RegisterRequest
    {
        public string Username { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public List<string>? Roles { get; set; } // Changed to List<string>
    }

    public class LoginRequest
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }

    public class AuthResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public UserDto? User { get; set; }
    }

    public class UserDto
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string Email { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public List<string> Roles { get; set; } // Changed to List<string>
        public bool IsActive { get; set; }
        public bool EmailConfirmed { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
    }

    public class ChangePasswordRequest
    {
        public string OldPassword { get; set; }
        public string NewPassword { get; set; }
    }

    public class AssignRolesRequest
    {
        public int UserId { get; set; }
        public List<string> Roles { get; set; }
    }

    public class RoleDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; }
    }
}