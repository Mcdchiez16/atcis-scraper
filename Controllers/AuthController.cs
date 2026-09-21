using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using ZimbabweTenderAPI.DTOs;
using ZimbabweTenderAPI.Services;

namespace ZimbabweTenderAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [ApiExplorerSettings(GroupName = "auth")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IAuthService authService, ILogger<AuthController> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        // Helper to get client IP and User Agent
        private (string? ipAddress, string? userAgent) GetClientContext()
        {
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = HttpContext.Request.Headers["User-Agent"].ToString();
            return (ipAddress, userAgent);
        }

        // POST: api/auth/register
        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<AuthResponse>>> Register([FromBody] RegisterRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new ApiResponse<AuthResponse>
                    {
                        Success = false,
                        Message = "Invalid request data"
                    });
                }

                var (ipAddress, userAgent) = GetClientContext();

                // PASS context to the service
                var result = await _authService.RegisterAsync(request, ipAddress, userAgent);

                if (!result.Success)
                {
                    return BadRequest(new ApiResponse<AuthResponse>
                    {
                        Success = false,
                        Message = result.Message
                    });
                }

                return Ok(new ApiResponse<AuthResponse>
                {
                    Success = true,
                    Message = "Registration successful",
                    Data = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during registration");
                return StatusCode(500, new ApiResponse<AuthResponse>
                {
                    Success = false,
                    Message = $"Registration error: {ex.Message}"
                });
            }
        }

        // POST: api/auth/login
        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<AuthResponse>>> Login([FromBody] LoginRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new ApiResponse<AuthResponse>
                    {
                        Success = false,
                        Message = "Invalid request data"
                    });
                }

                var (ipAddress, userAgent) = GetClientContext();

                // PASS context to the service
                var result = await _authService.LoginAsync(request, ipAddress, userAgent);

                if (!result.Success)
                {
                    return Unauthorized(new ApiResponse<AuthResponse>
                    {
                        Success = false,
                        Message = result.Message
                    });
                }

                return Ok(new ApiResponse<AuthResponse>
                {
                    Success = true,
                    Message = "Login successful",
                    Data = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login");
                return StatusCode(500, new ApiResponse<AuthResponse>
                {
                    Success = false,
                    Message = $"Login error: {ex.Message}"
                });
            }
        }

        // POST: api/auth/refresh
        [HttpPost("refresh")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<AuthResponse>>> RefreshToken([FromBody] RefreshTokenRequest request)
        {
            try
            {
                var (ipAddress, userAgent) = GetClientContext();

                // PASS context to the service
                var result = await _authService.RefreshTokenAsync(request.RefreshToken, ipAddress, userAgent);

                if (!result.Success)
                {
                    return Unauthorized(new ApiResponse<AuthResponse>
                    {
                        Success = false,
                        Message = result.Message
                    });
                }

                return Ok(new ApiResponse<AuthResponse>
                {
                    Success = true,
                    Message = "Token refreshed successfully",
                    Data = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing token");
                return StatusCode(500, new ApiResponse<AuthResponse>
                {
                    Success = false,
                    Message = $"Token refresh error: {ex.Message}"
                });
            }
        }

        // POST: api/auth/logout
        [HttpPost("logout")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<bool>>> Logout([FromBody] RefreshTokenRequest request)
        {
            try
            {
                var (ipAddress, userAgent) = GetClientContext();

                // PASS context to the service
                var result = await _authService.RevokeTokenAsync(request.RefreshToken, ipAddress, userAgent);

                return Ok(new ApiResponse<bool>
                {
                    Success = result,
                    Message = result ? "Logout successful" : "Logout failed",
                    Data = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during logout");
                return StatusCode(500, new ApiResponse<bool>
                {
                    Success = false,
                    Message = $"Logout error: {ex.Message}"
                });
            }
        }

        // GET: api/auth/me
        [HttpGet("me")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<UserDto>>> GetCurrentUser()
        {
            try
            {
                var username = User.Identity?.Name;
                if (string.IsNullOrEmpty(username))
                {
                    return Unauthorized(new ApiResponse<UserDto>
                    {
                        Success = false,
                        Message = "User not authenticated"
                    });
                }

                var user = await _authService.GetCurrentUserAsync(username);

                if (user == null)
                {
                    return NotFound(new ApiResponse<UserDto>
                    {
                        Success = false,
                        Message = "User not found"
                    });
                }

                return Ok(new ApiResponse<UserDto>
                {
                    Success = true,
                    Message = "User retrieved successfully",
                    Data = user
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current user");
                return StatusCode(500, new ApiResponse<UserDto>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        // POST: api/auth/change-password
        [HttpPost("change-password")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<bool>>> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            try
            {
                var username = User.Identity?.Name;
                if (string.IsNullOrEmpty(username))
                {
                    return Unauthorized(new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "User not authenticated"
                    });
                }

                var (ipAddress, userAgent) = GetClientContext();

                // PASS context to the service
                var result = await _authService.ChangePasswordAsync(username, request.OldPassword, request.NewPassword, ipAddress, userAgent);

                if (!result.Success)
                {
                    // FIX: Use the error message returned from the service
                    return BadRequest(new ApiResponse<bool>
                    {
                        Success = false,
                        Message = result.Message ?? "Password change failed. Please check your old password."
                    });
                }

                return Ok(new ApiResponse<bool>
                {
                    Success = true,
                    Message = "Password changed successfully",
                    Data = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error changing password");
                return StatusCode(500, new ApiResponse<bool>
                {
                    Success = false,
                    Message = $"Password change error: {ex.Message}"
                });
            }
        }

        // GET: api/auth/validate
        [HttpGet("validate")]
        [Authorize]
        public ActionResult<ApiResponse<object>> ValidateToken()
        {
            try
            {
                var username = User.Identity?.Name;
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                
                // Get all roles - the RoleClaimType is configured as the full URL
                var roleClaimType = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
                var roles = User.FindAll(roleClaimType).Select(c => c.Value).ToList();
                
                // Fallback to standard ClaimTypes.Role if no roles found
                if (!roles.Any())
                {
                    roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
                }

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Message = "Token is valid",
                    Data = new
                    {
                        Username = username,
                        UserId = userId,
                        Roles = roles,
                        IsAuthenticated = User.Identity?.IsAuthenticated ?? false
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating token");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = $"Token validation error: {ex.Message}"
                });
            }
        }

        // GET: api/auth/users - Get all users (Admin only)
        [HttpGet("users")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ActionResult<ApiResponse<List<UserDto>>>> GetAllUsers()
        {
            try
            {
                var users = await _authService.GetAllUsersAsync();

                return Ok(new ApiResponse<List<UserDto>>
                {
                    Success = true,
                    Message = $"Retrieved {users.Count} users",
                    Data = users,
                    TotalCount = users.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all users");
                return StatusCode(500, new ApiResponse<List<UserDto>>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        // GET: api/auth/users/{id}/roles - Get user's roles
        [HttpGet("users/{id}/roles")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ActionResult<ApiResponse<List<string>>>> GetUserRoles(int id)
        {
            try
            {
                var roles = await _authService.GetUserRolesAsync(id);

                if (roles == null)
                {
                    return NotFound(new ApiResponse<List<string>>
                    {
                        Success = false,
                        Message = "User not found"
                    });
                }

                return Ok(new ApiResponse<List<string>>
                {
                    Success = true,
                    Message = $"User has {roles.Count} role(s)",
                    Data = roles
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting roles for user {id}");
                return StatusCode(500, new ApiResponse<List<string>>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        // POST: api/auth/assign-role - Assign role to user (Admin only)
        [HttpPost("assign-role")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ActionResult<ApiResponse<bool>>> AssignRole([FromBody] RoleAssignmentRequest request)
        {
            try
            {
                var adminUsername = User.Identity?.Name;
                var result = await _authService.AssignRoleAsync(request.UserId, request.Role, adminUsername ?? "Unknown");

                if (!result.Success)
                {
                    return BadRequest(new ApiResponse<bool>
                    {
                        Success = false,
                        Message = result.Message
                    });
                }

                return Ok(new ApiResponse<bool>
                {
                    Success = true,
                    Message = result.Message,
                    Data = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error assigning role");
                return StatusCode(500, new ApiResponse<bool>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        // DELETE: api/auth/remove-role - Remove role from user (Admin only)
        [HttpDelete("remove-role")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ActionResult<ApiResponse<bool>>> RemoveRole([FromBody] RoleAssignmentRequest request)
        {
            try
            {
                var adminUsername = User.Identity?.Name;
                var result = await _authService.RemoveRoleAsync(request.UserId, request.Role, adminUsername ?? "Unknown");

                if (!result.Success)
                {
                    return BadRequest(new ApiResponse<bool>
                    {
                        Success = false,
                        Message = result.Message
                    });
                }

                return Ok(new ApiResponse<bool>
                {
                    Success = true,
                    Message = result.Message,
                    Data = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing role");
                return StatusCode(500, new ApiResponse<bool>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }
    }

    public class RoleAssignmentRequest
    {
        public int UserId { get; set; }
        public string Role { get; set; }
    }

    public class RefreshTokenRequest
    {
        public required string RefreshToken { get; set; }
    }
}