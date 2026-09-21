using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Collections.Generic;
using ZimbabweTenderAPI.Data;
using ZimbabweTenderAPI.Data.Entities;
using ZimbabweTenderAPI.DTOs;
using ZimbabweTenderAPI.Services;

namespace ZimbabweTenderAPI.Controllers
{
    /// <summary>
    /// Manages system roles and permissions
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [ApiExplorerSettings(GroupName = "auth")]
    //[Authorize(Roles = "Admin")]
    public class RolesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<RolesController> _logger;

        public RolesController(ApplicationDbContext context, ILogger<RolesController> logger)
        {
            _context = context;
            _logger = logger;
        }

        private string GetCurrentUsername()
        {
            return User.FindFirst(ClaimTypes.Name)?.Value ?? "System";
        }

        /// <summary>
        /// Get all available roles
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<List<RoleDto>>), 200)]
        public async Task<ActionResult<ApiResponse<List<RoleDto>>>> GetAllRoles()
        {
            try
            {
                var roles = await _context.Roles
                    .Where(r => !r.IsDeleted)
                    .OrderBy(r => r.Name)
                    .ToListAsync();

                var roleDtos = roles.Select(r => new RoleDto
                {
                    Id = r.Id,
                    Name = r.Name,
                    Description = r.Description,
                    IsActive = r.IsActive,
                    CreatedAt = r.CreatedAt,
                    UpdatedAt = r.UpdatedAt
                }).ToList();

                return Ok(new ApiResponse<List<RoleDto>>
                {
                    Success = true,
                    Data = roleDtos,
                    TotalCount = roleDtos.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving roles");
                return StatusCode(500, new ApiResponse<List<RoleDto>>
                {
                    Success = false,
                    Message = $"Error retrieving roles: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Get a specific role by ID
        /// </summary>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(ApiResponse<RoleDto>), 200)]
        [ProducesResponseType(404)]
        public async Task<ActionResult<ApiResponse<RoleDto>>> GetRoleById(int id)
        {
            try
            {
                var role = await _context.Roles
                    .Include(r => r.UserRoles)
                    .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);

                if (role == null)
                {
                    return NotFound(new ApiResponse<RoleDto>
                    {
                        Success = false,
                        Message = "Role not found"
                    });
                }

                var roleDto = new RoleDto
                {
                    Id = role.Id,
                    Name = role.Name,
                    Description = role.Description,
                    IsActive = role.IsActive,
                    CreatedAt = role.CreatedAt,
                    UpdatedAt = role.UpdatedAt,
                    UserCount = role.UserRoles?.Count ?? 0
                };

                return Ok(new ApiResponse<RoleDto>
                {
                    Success = true,
                    Data = roleDto
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving role {id}");
                return StatusCode(500, new ApiResponse<RoleDto>
                {
                    Success = false,
                    Message = $"Error retrieving role: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Create a new role
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "SuperAdmin")]
        [ProducesResponseType(typeof(ApiResponse<RoleDto>), 201)]
        [ProducesResponseType(400)]
        public async Task<ActionResult<ApiResponse<RoleDto>>> CreateRole([FromBody] CreateRoleRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new ApiResponse<RoleDto>
                    {
                        Success = false,
                        Message = "Invalid request data"
                    });
                }

                // Check if role already exists
                if (await _context.Roles.AnyAsync(r => r.Name == request.Name))
                {
                    return BadRequest(new ApiResponse<RoleDto>
                    {
                        Success = false,
                        Message = "Role already exists"
                    });
                }

                var role = new RoleEntity
                {
                    Name = request.Name,
                    Description = request.Description,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = GetCurrentUsername()
                };

                await _context.Roles.AddAsync(role);
                await _context.SaveChangesAsync();

                var roleDto = new RoleDto
                {
                    Id = role.Id,
                    Name = role.Name,
                    Description = role.Description,
                    IsActive = role.IsActive,
                    CreatedAt = role.CreatedAt
                };

                _logger.LogInformation($"Role '{role.Name}' created by {GetCurrentUsername()}");

                return CreatedAtAction(nameof(GetRoleById), new { id = role.Id }, new ApiResponse<RoleDto>
                {
                    Success = true,
                    Message = "Role created successfully",
                    Data = roleDto
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating role");
                return StatusCode(500, new ApiResponse<RoleDto>
                {
                    Success = false,
                    Message = $"Error creating role: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Update an existing role
        /// </summary>
        [HttpPut("{id}")]
        [Authorize(Roles = "SuperAdmin")]
        [ProducesResponseType(typeof(ApiResponse<RoleDto>), 200)]
        [ProducesResponseType(404)]
        public async Task<ActionResult<ApiResponse<RoleDto>>> UpdateRole(int id, [FromBody] UpdateRoleRequest request)
        {
            try
            {
                var role = await _context.Roles.FindAsync(id);

                if (role == null || role.IsDeleted)
                {
                    return NotFound(new ApiResponse<RoleDto>
                    {
                        Success = false,
                        Message = "Role not found"
                    });
                }

                // Prevent modifying system roles
                var systemRoles = new[] { "SuperAdmin", "Admin", "Employee" };
                if (systemRoles.Contains(role.Name))
                {
                    return BadRequest(new ApiResponse<RoleDto>
                    {
                        Success = false,
                        Message = "System roles cannot be modified"
                    });
                }

                if (!string.IsNullOrWhiteSpace(request.Description))
                    role.Description = request.Description;

                if (request.IsActive.HasValue)
                    role.IsActive = request.IsActive.Value;

                role.UpdatedAt = DateTime.UtcNow;
                role.UpdatedBy = GetCurrentUsername();

                await _context.SaveChangesAsync();

                var roleDto = new RoleDto
                {
                    Id = role.Id,
                    Name = role.Name,
                    Description = role.Description,
                    IsActive = role.IsActive,
                    CreatedAt = role.CreatedAt,
                    UpdatedAt = role.UpdatedAt
                };

                _logger.LogInformation($"Role '{role.Name}' updated by {GetCurrentUsername()}");

                return Ok(new ApiResponse<RoleDto>
                {
                    Success = true,
                    Message = "Role updated successfully",
                    Data = roleDto
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating role {id}");
                return StatusCode(500, new ApiResponse<RoleDto>
                {
                    Success = false,
                    Message = $"Error updating role: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Delete a role (soft delete)
        /// </summary>
        [HttpDelete("{id}")]
        [Authorize(Roles = "SuperAdmin")]
        [ProducesResponseType(typeof(ApiResponse<object>), 200)]
        [ProducesResponseType(404)]
        public async Task<ActionResult<ApiResponse<object>>> DeleteRole(int id)
        {
            try
            {
                var role = await _context.Roles
                    .Include(r => r.UserRoles)
                    .FirstOrDefaultAsync(r => r.Id == id);

                if (role == null || role.IsDeleted)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Role not found"
                    });
                }

                // Prevent deleting system roles
                var systemRoles = new[] { "SuperAdmin", "Admin", "Employee" };
                if (systemRoles.Contains(role.Name))
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "System roles cannot be deleted"
                    });
                }

                // Check if role has users
                if (role.UserRoles != null && role.UserRoles.Any())
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = $"Cannot delete role: {role.UserRoles.Count} user(s) assigned to this role"
                    });
                }

                role.IsDeleted = true;
                role.DeletedAt = DateTime.UtcNow;
                role.DeletedBy = GetCurrentUsername();

                await _context.SaveChangesAsync();

                _logger.LogInformation($"Role '{role.Name}' deleted by {GetCurrentUsername()}");

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Message = "Role deleted successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting role {id}");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = $"Error deleting role: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Get all users assigned to a specific role
        /// </summary>
        [HttpGet("{id}/users")]
        [ProducesResponseType(typeof(ApiResponse<List<UserDto>>), 200)]
        [ProducesResponseType(404)]
        public async Task<ActionResult<ApiResponse<List<UserDto>>>> GetRoleUsers(int id)
        {
            try
            {
                var role = await _context.Roles
                    .Include(r => r.UserRoles)
                        .ThenInclude(ur => ur.User)
                    .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);

                if (role == null)
                {
                    return NotFound(new ApiResponse<List<UserDto>>
                    {
                        Success = false,
                        Message = "Role not found"
                    });
                }

                var users = role.UserRoles?
                    .Where(ur => !ur.User.IsDeleted)
                    .Select(ur => new UserDto
                    {
                        Id = ur.User.Id,
                        Username = ur.User.Username,
                        Email = ur.User.Email,
                        FirstName = ur.User.FirstName,
                        LastName = ur.User.LastName,
                        IsActive = ur.User.IsActive,
                        EmailConfirmed = ur.User.EmailConfirmed,
                        CreatedAt = ur.User.CreatedAt
                    })
                    .ToList() ?? new List<UserDto>();

                return Ok(new ApiResponse<List<UserDto>>
                {
                    Success = true,
                    Data = users,
                    TotalCount = users.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving users for role {id}");
                return StatusCode(500, new ApiResponse<List<UserDto>>
                {
                    Success = false,
                    Message = $"Error retrieving users: {ex.Message}"
                });
            }
        }
    }

    // DTOs for Role Management
    public class RoleDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public int UserCount { get; set; }
    }

    public class CreateRoleRequest
    {
        [Required]
        [MaxLength(50)]
        public string Name { get; set; }

        [MaxLength(200)]
        public string? Description { get; set; }
    }

    public class UpdateRoleRequest
    {
        [MaxLength(200)]
        public string? Description { get; set; }

        public bool? IsActive { get; set; }
    }
}
