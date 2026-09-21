using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ZimbabweTenderAPI.Models;
using ZimbabweTenderAPI.DTOs;
using ZimbabweTenderAPI.Services;

namespace ZimbabweTenderAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Route("api/zambia/multi-source")]
    [Authorize]
    [ApiExplorerSettings(GroupName = "zambia")]
    [Tags("Zambia Multi-Source Procurement")]
    public class MultiSourceProcurementController : ControllerBase
    {
        private readonly IMultiSourceProcurementService _procurementService;
        private readonly ILogger<MultiSourceProcurementController> _logger;

        public MultiSourceProcurementController(
            IMultiSourceProcurementService procurementService,
            ILogger<MultiSourceProcurementController> logger)
        {
            _procurementService = procurementService;
            _logger = logger;
        }

        /// <summary>
        /// Unified multi-source procurement search aggregating Zambia and International institutions (ZPPA, GoZambiaJobs, OnlineTenders, World Bank, UN, EU, DevelopmentAid).
        /// </summary>
        /// <param name="keyword">Optional keyword filter across title, entity, commodity, or description</param>
        /// <param name="country">Optional country filter (e.g. "Zambia", "all")</param>
        /// <param name="portal">Optional specific portal (e.g. "WorldBank", "UNProcurement", "GoZambiaJobs", "OnlineTenders", "EUFunding", "all")</param>
        /// <param name="limit">Maximum number of results to return (default 50)</param>
        [HttpGet("unified")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<List<MultiSourceTenderDto>>>> GetUnifiedProcurementFeed(
            [FromQuery] string? keyword = null,
            [FromQuery] string? country = null,
            [FromQuery] string? portal = null,
            [FromQuery] int limit = 50)
        {
            try
            {
                var tenders = await _procurementService.GetUnifiedProcurementFeedAsync(keyword, country, portal, limit);

                return Ok(new ApiResponse<List<MultiSourceTenderDto>>
                {
                    Success = true,
                    Message = $"Retrieved {tenders.Count} aggregated procurement opportunities across multi-source portals",
                    Data = tenders,
                    TotalCount = tenders.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting unified procurement feed");
                return StatusCode(500, new ApiResponse<List<MultiSourceTenderDto>>
                {
                    Success = false,
                    Message = $"Error retrieving multi-source procurement feed: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Scrape live tenders, RFPs, and Google Drive specification documents from GoZambiaJobs.
        /// </summary>
        /// <param name="page">Page number</param>
        /// <param name="keyword">Optional keyword filter</param>
        [HttpGet("go-zambia-jobs")]
        public async Task<ActionResult<ApiResponse<List<MultiSourceTenderDto>>>> GetGoZambiaJobsTenders(
            [FromQuery] int page = 1,
            [FromQuery] string? keyword = null)
        {
            try
            {
                var tenders = await _procurementService.GetGoZambiaJobsTendersAsync(page, keyword);

                return Ok(new ApiResponse<List<MultiSourceTenderDto>>
                {
                    Success = true,
                    Message = $"Retrieved {tenders.Count} tenders & RFPs from GoZambiaJobs",
                    Data = tenders,
                    TotalCount = tenders.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting GoZambiaJobs tenders");
                return StatusCode(500, new ApiResponse<List<MultiSourceTenderDto>>
                {
                    Success = false,
                    Message = $"Error retrieving GoZambiaJobs tenders: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Scrape live Zambia tenders from OnlineTenders.co.za.
        /// </summary>
        /// <param name="page">Page number (default 1)</param>
        /// <param name="keyword">Optional keyword filter</param>
        [HttpGet("online-tenders-zambia")]
        public async Task<ActionResult<ApiResponse<List<MultiSourceTenderDto>>>> GetOnlineTendersZambia(
            [FromQuery] int page = 1,
            [FromQuery] string? keyword = null)
        {
            try
            {
                var tenders = await _procurementService.GetOnlineTendersZambiaAsync(page, keyword);

                return Ok(new ApiResponse<List<MultiSourceTenderDto>>
                {
                    Success = true,
                    Message = $"Retrieved {tenders.Count} tenders from OnlineTenders Zambia (Page {page})",
                    Data = tenders,
                    TotalCount = tenders.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting OnlineTenders Zambia");
                return StatusCode(500, new ApiResponse<List<MultiSourceTenderDto>>
                {
                    Success = false,
                    Message = $"Error retrieving OnlineTenders Zambia: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Scrape live Zimbabwe tenders from OnlineTenders.co.za.
        /// </summary>
        /// <param name="page">Page number (default 1)</param>
        /// <param name="keyword">Optional keyword filter</param>
        [HttpGet("online-tenders-zimbabwe")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<List<MultiSourceTenderDto>>>> GetOnlineTendersZimbabwe(
            [FromQuery] int page = 1,
            [FromQuery] string? keyword = null)
        {
            try
            {
                var tenders = await _procurementService.GetOnlineTendersZimbabweAsync(page, keyword);

                return Ok(new ApiResponse<List<MultiSourceTenderDto>>
                {
                    Success = true,
                    Message = $"Retrieved {tenders.Count} tenders from OnlineTenders Zimbabwe (Page {page})",
                    Data = tenders,
                    TotalCount = tenders.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting OnlineTenders Zimbabwe");
                return StatusCode(500, new ApiResponse<List<MultiSourceTenderDto>>
                {
                    Success = false,
                    Message = $"Error retrieving OnlineTenders Zimbabwe: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Get United Nations Global Marketplace (UNGM / UNDP) Zimbabwe tenders and RFPs.
        /// </summary>
        [HttpGet("ungm-zimbabwe")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<List<MultiSourceTenderDto>>>> GetUngmZimbabwe(
            [FromQuery] string? keyword = null)
        {
            try
            {
                var tenders = await _procurementService.GetUngmZimbabweTendersAsync(keyword);

                return Ok(new ApiResponse<List<MultiSourceTenderDto>>
                {
                    Success = true,
                    Message = $"Retrieved {tenders.Count} tenders from UNGM / UN Zimbabwe",
                    Data = tenders,
                    TotalCount = tenders.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting UNGM Zimbabwe tenders");
                return StatusCode(500, new ApiResponse<List<MultiSourceTenderDto>>
                {
                    Success = false,
                    Message = $"Error retrieving UNGM Zimbabwe tenders: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Get African Development Bank (AfDB) Zimbabwe project notices.
        /// </summary>
        [HttpGet("afdb-zimbabwe")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<List<MultiSourceTenderDto>>>> GetAfdbZimbabwe(
            [FromQuery] string? keyword = null)
        {
            try
            {
                var tenders = await _procurementService.GetAfdbZimbabweTendersAsync(keyword);

                return Ok(new ApiResponse<List<MultiSourceTenderDto>>
                {
                    Success = true,
                    Message = $"Retrieved {tenders.Count} tenders from AfDB Zimbabwe",
                    Data = tenders,
                    TotalCount = tenders.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting AfDB Zimbabwe tenders");
                return StatusCode(500, new ApiResponse<List<MultiSourceTenderDto>>
                {
                    Success = false,
                    Message = $"Error retrieving AfDB Zimbabwe tenders: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Get official World Bank Procurement notices (Tenders, Expressions of Interest, Requests for Bids) for Zambia or globally.
        /// </summary>
        /// <param name="countryCode">ISO 2-letter country code (default "ZM" for Zambia, or "all")</param>
        /// <param name="page">Page number (default 1)</param>
        /// <param name="pageSize">Results per page (default 20)</param>
        /// <param name="keyword">Optional keyword search</param>
        [HttpGet("world-bank")]
        public async Task<ActionResult<ApiResponse<List<MultiSourceTenderDto>>>> GetWorldBankTenders(
            [FromQuery] string countryCode = "ZM",
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? keyword = null)
        {
            try
            {
                var tenders = await _procurementService.GetWorldBankTendersAsync(countryCode, page, pageSize, keyword);

                return Ok(new ApiResponse<List<MultiSourceTenderDto>>
                {
                    Success = true,
                    Message = $"Retrieved {tenders.Count} World Bank procurement notices (Country: {countryCode})",
                    Data = tenders,
                    TotalCount = tenders.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting World Bank procurement notices");
                return StatusCode(500, new ApiResponse<List<MultiSourceTenderDto>>
                {
                    Success = false,
                    Message = $"Error retrieving World Bank procurement notices: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Get United Nations (UN) Global Procurement Division Solicitations, Tenders, and Expressions of Interest (with direct official UN PDF links).
        /// </summary>
        /// <param name="type">Notice type: "tender", "eoi", or "all" (default "all")</param>
        /// <param name="keyword">Optional keyword filter</param>
        [HttpGet("un-procurement")]
        public async Task<ActionResult<ApiResponse<List<MultiSourceTenderDto>>>> GetUnProcurementNotices(
            [FromQuery] string type = "all",
            [FromQuery] string? keyword = null)
        {
            try
            {
                var notices = await _procurementService.GetUnProcurementNoticesAsync(type, keyword);

                return Ok(new ApiResponse<List<MultiSourceTenderDto>>
                {
                    Success = true,
                    Message = $"Retrieved {notices.Count} UN procurement solicitations ({type})",
                    Data = notices,
                    TotalCount = notices.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting UN procurement notices");
                return StatusCode(500, new ApiResponse<List<MultiSourceTenderDto>>
                {
                    Success = false,
                    Message = $"Error retrieving UN procurement notices: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Get European Commission (EU) Calls for Tenders and international grant opportunities from the EU Funding &amp; Tenders Portal.
        /// </summary>
        /// <param name="pageNumber">Page number (default 1)</param>
        /// <param name="pageSize">Page size (default 20)</param>
        /// <param name="keyword">Optional keyword query</param>
        [HttpGet("eu-funding")]
        public async Task<ActionResult<ApiResponse<List<MultiSourceTenderDto>>>> GetEuFundingTenders(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? keyword = null)
        {
            try
            {
                var tenders = await _procurementService.GetEuFundingTendersAsync(pageNumber, pageSize, keyword);

                return Ok(new ApiResponse<List<MultiSourceTenderDto>>
                {
                    Success = true,
                    Message = $"Retrieved {tenders.Count} EU Funding Calls for Tenders",
                    Data = tenders,
                    TotalCount = tenders.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting EU Funding tenders");
                return StatusCode(500, new ApiResponse<List<MultiSourceTenderDto>>
                {
                    Success = false,
                    Message = $"Error retrieving EU Funding tenders: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Get International Development tenders &amp; grants from DevelopmentAid.
        /// </summary>
        /// <param name="type">"tenders" or "grants" (default "tenders")</param>
        /// <param name="page">Page number</param>
        /// <param name="keyword">Optional keyword query</param>
        [HttpGet("development-aid")]
        public async Task<ActionResult<ApiResponse<List<MultiSourceTenderDto>>>> GetDevelopmentAidTenders(
            [FromQuery] string type = "tenders",
            [FromQuery] int page = 1,
            [FromQuery] string? keyword = null)
        {
            try
            {
                var tenders = await _procurementService.GetDevelopmentAidTendersAsync(type, page, keyword);

                return Ok(new ApiResponse<List<MultiSourceTenderDto>>
                {
                    Success = true,
                    Message = $"Retrieved {tenders.Count} DevelopmentAid {type}",
                    Data = tenders,
                    TotalCount = tenders.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting DevelopmentAid tenders");
                return StatusCode(500, new ApiResponse<List<MultiSourceTenderDto>>
                {
                    Success = false,
                    Message = $"Error retrieving DevelopmentAid opportunities: {ex.Message}"
                });
            }
        }
    }
}
