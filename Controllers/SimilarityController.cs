using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZimbabweTenderAPI.DTOs;
using ZimbabweTenderAPI.Services;

namespace ZimbabweTenderAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SimilarityController : ControllerBase
    {
        private readonly TenderSimilarityService _similarityService;
        private readonly ILogger<SimilarityController> _logger;

        public SimilarityController(
            TenderSimilarityService similarityService,
            ILogger<SimilarityController> logger)
        {
            _similarityService = similarityService;
            _logger = logger;
        }

        // GET: api/similarity/tenders/{tenderId}
        [HttpGet("tenders/{tenderId}")]
        public async Task<ActionResult<ApiResponse<List<TenderSimilarityDto>>>> GetTenderSimilarity(string tenderId)
        {
            try
            {
                if (string.IsNullOrEmpty(tenderId))
                {
                    return BadRequest(new ApiResponse<List<TenderSimilarityDto>>
                    {
                        Success = false,
                        Message = "Tender ID is required"
                    });
                }

                var similarities = await _similarityService.AnalyzeTenderSimilarityAsync(tenderId);

                return Ok(new ApiResponse<List<TenderSimilarityDto>>
                {
                    Success = true,
                    Message = $"Successfully analyzed similarity for tender {tenderId}",
                    Data = similarities ?? new List<TenderSimilarityDto>(),
                    TotalCount = similarities?.Count ?? 0
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error analyzing similarity for tender {tenderId}");
                return StatusCode(500, new ApiResponse<List<TenderSimilarityDto>>
                {
                    Success = false,
                    Message = $"Error analyzing similarity: {ex.Message}",
                    Data = new List<TenderSimilarityDto>(),
                    TotalCount = 0
                });
            }
        }

        // POST: api/similarity/tenders
        [HttpPost("tenders")]
        public async Task<ActionResult<ApiResponse<List<TenderSimilarityDto>>>> FindSimilarTenders(
            [FromBody] SimilarityAnalysisRequest request)
        {
            try
            {
                if (request == null)
                {
                    return BadRequest(new ApiResponse<List<TenderSimilarityDto>>
                    {
                        Success = false,
                        Message = "Request is required"
                    });
                }

                List<TenderSimilarityDto> similarities;

                if (!string.IsNullOrEmpty(request.TenderId))
                {
                    similarities = await _similarityService.AnalyzeTenderSimilarityAsync(request.TenderId);
                }
                else if (request.Keywords?.Any() == true)
                {
                    similarities = await _similarityService.FindSimilarTendersAsync(
                        request.Keywords.FirstOrDefault() ?? "",
                        10);

                    if (request.MinSimilarityThreshold > 0)
                    {
                        similarities = similarities?
                            .Where(s => s.SimilarityScore >= request.MinSimilarityThreshold)
                            .ToList() ?? new List<TenderSimilarityDto>();
                    }
                }
                else
                {
                    return BadRequest(new ApiResponse<List<TenderSimilarityDto>>
                    {
                        Success = false,
                        Message = "Either tenderId or keywords are required"
                    });
                }

                return Ok(new ApiResponse<List<TenderSimilarityDto>>
                {
                    Success = true,
                    Message = "Successfully found similar tenders",
                    Data = similarities ?? new List<TenderSimilarityDto>(),
                    TotalCount = similarities?.Count ?? 0
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finding similar tenders");
                return StatusCode(500, new ApiResponse<List<TenderSimilarityDto>>
                {
                    Success = false,
                    Message = $"Error finding similar tenders: {ex.Message}",
                    Data = new List<TenderSimilarityDto>(),
                    TotalCount = 0
                });
            }
        }

        // GET: api/similarity/companies/{companyName}/participation
        [HttpGet("companies/{companyName}/participation")]
        public async Task<ActionResult<ApiResponse<List<TenderParticipationDto>>>> GetCompanyParticipation(string companyName)
        {
            try
            {
                var participation = await _similarityService.GetCompanyParticipationAsync(companyName);

                return Ok(new ApiResponse<List<TenderParticipationDto>>
                {
                    Success = true,
                    Message = $"Successfully retrieved participation data for {companyName}",
                    Data = participation,
                    TotalCount = participation.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting participation for company {companyName}");
                return StatusCode(500, new ApiResponse<List<TenderParticipationDto>>
                {
                    Success = false,
                    Message = $"Error getting participation: {ex.Message}"
                });
            }
        }

        // GET: api/similarity/companies/top-participants?limit=20
        [HttpGet("companies/top-participants")]
        public async Task<ActionResult<ApiResponse<List<TenderParticipationDto>>>> GetTopParticipatingCompanies(
            [FromQuery] int limit = 20)
        {
            try
            {
                var topCompanies = await _similarityService.GetTopParticipatingCompaniesAsync(limit);

                return Ok(new ApiResponse<List<TenderParticipationDto>>
                {
                    Success = true,
                    Message = "Successfully retrieved top participating companies",
                    Data = topCompanies,
                    TotalCount = topCompanies.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting top participating companies");
                return StatusCode(500, new ApiResponse<List<TenderParticipationDto>>
                {
                    Success = false,
                    Message = $"Error getting top participants: {ex.Message}"
                });
            }
        }

        // GET: api/similarity/companies/fuzzy-search?name=ZIMBABWE%20ELECTRITY&threshold=0.7
        [HttpGet("companies/fuzzy-search")]
        public async Task<ActionResult<ApiResponse<List<TenderParticipationDto>>>> FuzzySearchCompany(
            [FromQuery] string name,
            [FromQuery] double threshold = 0.7,
            [FromQuery] int limit = 10)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    return BadRequest(new ApiResponse<List<TenderParticipationDto>>
                    {
                        Success = false,
                        Message = "Company name is required"
                    });
                }

                // Get top participating companies
                var topCompanies = await _similarityService.GetTopParticipatingCompaniesAsync(50);

                // Find companies with similar names
                var similarCompanies = new List<TenderParticipationDto>();

                foreach (var company in topCompanies)
                {
                    var similarity = await _similarityService.CalculateCompanyNameSimilarityAsync(
                        company.CompanyName, name);

                    if (similarity >= threshold)
                    {
                        // Get participation details for this company
                        var participation = await _similarityService.GetCompanyParticipationAsync(company.CompanyName);

                        // Add similarity score to each result
                        foreach (var item in participation)
                        {
                            item.SimilarityScore = similarity;
                        }

                        similarCompanies.AddRange(participation);
                    }
                }

                // Get unique results sorted by similarity
                var results = similarCompanies
                    .GroupBy(c => c.TenderId)
                    .Select(g => g.First())
                    .OrderByDescending(c => c.SimilarityScore)
                    .ThenByDescending(c => c.PublishDate)
                    .Take(limit)
                    .ToList();

                return Ok(new ApiResponse<List<TenderParticipationDto>>
                {
                    Success = true,
                    Message = $"Found {results.Count} tenders for companies similar to '{name}'",
                    Data = results,
                    TotalCount = results.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in fuzzy search for company: '{name}'");
                return StatusCode(500, new ApiResponse<List<TenderParticipationDto>>
                {
                    Success = false,
                    Message = $"Error searching company: {ex.Message}"
                });
            }
        }
    }
}