using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZimbabweTenderAPI.Data;
using ZimbabweTenderAPI.Data.Entities;
using ZimbabweTenderAPI.DTOs;
using ZimbabweTenderAPI.Services;

namespace ZimbabweTenderAPI.Controllers
{
    // ==================== USER MANAGEMENT CONTROLLER ====================
    [ApiController]
    [Route("api/[controller]")]
    //[Authorize(Policy = "AdminOnly")]
    [ApiExplorerSettings(GroupName = "auth")]
    public class UsersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<UsersController> _logger;

        public UsersController(ApplicationDbContext context, ILogger<UsersController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<UserDto>>>> GetAll()
        {
            var users = await _context.Users
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .Select(u => new UserDto
                {
                    Id = u.Id,
                    Username = u.Username,
                    Email = u.Email,
                    FirstName = u.FirstName,
                    LastName = u.LastName,
                    Roles = u.UserRoles.Select(ur => ur.Role.Name).ToList(),
                    IsActive = u.IsActive,
                    CreatedAt = u.CreatedAt,
                    LastLoginAt = u.LastLoginAt
                }).ToListAsync();

            return Ok(new ApiResponse<List<UserDto>> { Success = true, Data = users });
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResponse<UserDto>>> GetById(int id)
        {
            var user = await _context.Users
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.Id == id);

            if (user == null) 
                return NotFound(new ApiResponse<UserDto> { Success = false, Message = "User not found" });

            return Ok(new ApiResponse<UserDto>
            {
                Success = true,
                Data = new UserDto
                {
                    Id = user.Id,
                    Username = user.Username,
                    Email = user.Email,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    Roles = user.UserRoles.Select(ur => ur.Role.Name).ToList(),
                    IsActive = user.IsActive,
                    CreatedAt = user.CreatedAt,
                    LastLoginAt = user.LastLoginAt
                }
            });
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<UserDto>>> Create([FromBody] RegisterRequest request)
        {
            var user = new UserEntity
            {
                Username = request.Username,
                Email = request.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                FirstName = request.FirstName,
                LastName = request.LastName,
                IsActive = true
            };

            await _context.Users.AddAsync(user);
            await _context.SaveChangesAsync();

            // Assign roles
            var rolesToAssign = request.Roles != null && request.Roles.Any() 
                ? request.Roles 
                : new List<string> { "Employee" };

            foreach (var roleName in rolesToAssign)
            {
                var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == roleName);
                if (role != null)
                {
                    await _context.UserRoles.AddAsync(new UserRoleEntity
                    {
                        UserId = user.Id,
                        RoleId = role.Id,
                        AssignedBy = User.Identity.Name ?? "Admin"
                    });
                }
            }

            await _context.SaveChangesAsync();

            return Ok(new ApiResponse<UserDto> { Success = true, Message = "User created" });
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<ApiResponse<bool>>> Update(int id, [FromBody] UserDto dto)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) 
                return NotFound(new ApiResponse<bool> { Success = false, Message = "User not found" });

            user.FirstName = dto.FirstName;
            user.LastName = dto.LastName;
            user.Email = dto.Email;
            user.IsActive = dto.IsActive;

            await _context.SaveChangesAsync();
            return Ok(new ApiResponse<bool> { Success = true, Data = true });
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult<ApiResponse<bool>>> Delete(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) 
                return NotFound(new ApiResponse<bool> { Success = false, Message = "User not found" });

            user.IsDeleted = true;
            await _context.SaveChangesAsync();
            return Ok(new ApiResponse<bool> { Success = true, Data = true });
        }

        [HttpPost("{id}/activate")]
        public async Task<ActionResult<ApiResponse<bool>>> Activate(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();
            user.IsActive = true;
            await _context.SaveChangesAsync();
            return Ok(new ApiResponse<bool> { Success = true, Data = true });
        }

        [HttpPost("{id}/deactivate")]
        public async Task<ActionResult<ApiResponse<bool>>> Deactivate(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();
            user.IsActive = false;
            await _context.SaveChangesAsync();
            return Ok(new ApiResponse<bool> { Success = true, Data = true });
        }

        // ==================== ROLE MANAGEMENT ====================
        
        [HttpGet("roles")]
        public async Task<ActionResult<ApiResponse<List<RoleDto>>>> GetAllRoles()
        {
            var roles = await _context.Roles
                .Select(r => new RoleDto
                {
                    Id = r.Id,
                    Name = r.Name,
                    Description = r.Description,
                    IsActive = r.IsActive
                }).ToListAsync();

            return Ok(new ApiResponse<List<RoleDto>> { Success = true, Data = roles });
        }

        [HttpPost("{userId}/roles")]
        public async Task<ActionResult<ApiResponse<bool>>> AssignRoles(int userId, [FromBody] AssignRolesRequest request)
        {
            var user = await _context.Users
                .Include(u => u.UserRoles)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                return NotFound(new ApiResponse<bool> { Success = false, Message = "User not found" });

            // Remove existing roles
            _context.UserRoles.RemoveRange(user.UserRoles);

            // Add new roles
            foreach (var roleName in request.Roles)
            {
                var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == roleName);
                if (role != null)
                {
                    await _context.UserRoles.AddAsync(new UserRoleEntity
                    {
                        UserId = userId,
                        RoleId = role.Id,
                        AssignedBy = User.Identity.Name ?? "Admin"
                    });
                }
            }

            await _context.SaveChangesAsync();
            return Ok(new ApiResponse<bool> { Success = true, Message = "Roles assigned successfully", Data = true });
        }

        [HttpGet("{userId}/roles")]
        public async Task<ActionResult<ApiResponse<List<string>>>> GetUserRoles(int userId)
        {
            var roles = await _context.UserRoles
                .Where(ur => ur.UserId == userId)
                .Include(ur => ur.Role)
                .Select(ur => ur.Role.Name)
                .ToListAsync();

            return Ok(new ApiResponse<List<string>> { Success = true, Data = roles });
        }

        [HttpDelete("{userId}/roles/{roleName}")]
        public async Task<ActionResult<ApiResponse<bool>>> RemoveRole(int userId, string roleName)
        {
            var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == roleName);
            if (role == null)
                return NotFound(new ApiResponse<bool> { Success = false, Message = "Role not found" });

            var userRole = await _context.UserRoles
                .FirstOrDefaultAsync(ur => ur.UserId == userId && ur.RoleId == role.Id);

            if (userRole == null)
                return NotFound(new ApiResponse<bool> { Success = false, Message = "User does not have this role" });

            _context.UserRoles.Remove(userRole);
            await _context.SaveChangesAsync();

            return Ok(new ApiResponse<bool> { Success = true, Message = "Role removed successfully", Data = true });
        }
    }

}
