using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using ZimbabweTenderAPI.Services;

namespace ZimbabweTenderAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [ApiExplorerSettings(GroupName = "system")]
    [Authorize(Policy = "ProjectManagerAccess")]
    public class AnalyticsController : ControllerBase
    {
        private readonly ITenderAnalysisService _analysisService;

        public AnalyticsController(ITenderAnalysisService analysisService)
        {
            _analysisService = analysisService;
        }

        /// <summary>
        /// Analyze a tender and get participation recommendation
        /// </summary>
        /// <param name="tenderId">The tender ID to analyze</param>
        /// <returns>Detailed recommendation including confidence score, preparation requirements, and competitors</returns>
        [HttpGet("tender/{tenderId}/recommendation")]
        public async Task<IActionResult> GetTenderRecommendation(string tenderId)
        {
            try
            {
                var recommendation = await _analysisService.AnalyzeTenderForParticipation(tenderId);
                return Ok(recommendation);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Identify business opportunities from recent awards and procurement plans
        /// </summary>
        /// <param name="days">Number of days to look back (default: 90)</param>
        /// <returns>List of identified business opportunities with confidence scores</returns>
        [HttpGet("opportunities")]
        public async Task<IActionResult> GetBusinessOpportunities([FromQuery] int days = 90)
        {
            try
            {
                var opportunities = await _analysisService.IdentifyBusinessOpportunities(days);
                return Ok(opportunities);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Analyze tender patterns and trends
        /// </summary>
        /// <param name="categoryCode">Optional category code to filter by</param>
        /// <param name="monthsBack">Number of months to analyze (default: 6)</param>
        /// <returns>Comprehensive pattern analysis including top winners, trends, and distributions</returns>
        [HttpGet("patterns")]
        public async Task<IActionResult> GetTenderPatterns(
            [FromQuery] string? categoryCode = null,
            [FromQuery] int monthsBack = 6)
        {
            try
            {
                var patterns = await _analysisService.AnalyzeTenderPatterns(categoryCode, monthsBack);
                return Ok(patterns);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Generate stakeholder alerts for new tenders, closing deadlines, and awards
        /// </summary>
        /// <returns>Prioritized list of alerts requiring stakeholder attention</returns>
        [HttpGet("alerts")]
        public async Task<IActionResult> GetStakeholderAlerts()
        {
            try
            {
                var alerts = await _analysisService.GenerateStakeholderAlerts();
                return Ok(new
                {
                    generatedAt = DateTime.UtcNow,
                    totalAlerts = alerts.Count,
                    alerts = alerts
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
