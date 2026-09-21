using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using ZimbabweTenderAPI.DTOs;
using ZimbabweTenderAPI.Services;

namespace ZimbabweTenderAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "GuestAccess")]
    [ApiExplorerSettings(GroupName = "system")]
    public class StatisticsController : ControllerBase
    {
        private readonly ITenderStatsService _statsService;
        private readonly ILogger<StatisticsController> _logger;

        public StatisticsController(
            ITenderStatsService statsService,
            ILogger<StatisticsController> logger)
        {
            _statsService = statsService;
            _logger = logger;
        }

        // GET: api/statistics/comprehensive
        [HttpGet("comprehensive")]
        public async Task<ActionResult<ApiResponse<object>>> GetComprehensiveStats()
        {
            try
            {
                var stats = await _statsService.GetComprehensiveStatsAsync();

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Message = "Comprehensive system statistics",
                    Data = stats
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting comprehensive stats");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }
    }
}