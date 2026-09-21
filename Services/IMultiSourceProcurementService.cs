using System.Collections.Generic;
using System.Threading.Tasks;
using ZimbabweTenderAPI.Models;

namespace ZimbabweTenderAPI.Services
{
    public interface IMultiSourceProcurementService
    {
        Task<List<MultiSourceTenderDto>> GetGoZambiaJobsTendersAsync(int page = 1, string? keyword = null);
        Task<List<MultiSourceTenderDto>> GetOnlineTendersZambiaAsync(int page = 1, string? keyword = null);
        Task<List<MultiSourceTenderDto>> GetOnlineTendersZimbabweAsync(int page = 1, string? keyword = null);
        Task<List<MultiSourceTenderDto>> GetWorldBankTendersAsync(string countryCode = "ZM", int page = 1, int pageSize = 20, string? keyword = null);
        Task<List<MultiSourceTenderDto>> GetUnProcurementNoticesAsync(string noticeType = "all", string? keyword = null);
        Task<List<MultiSourceTenderDto>> GetUngmCountryTendersAsync(string countryCode = "ZW", int page = 1, string? keyword = null);
        Task<List<MultiSourceTenderDto>> GetUngmZimbabweTendersAsync(string? keyword = null);
        Task<List<MultiSourceTenderDto>> GetUngmZambiaTendersAsync(string? keyword = null);
        Task<List<MultiSourceTenderDto>> GetAfdbZimbabweTendersAsync(string? keyword = null);
        Task<List<MultiSourceTenderDto>> GetEuFundingTendersAsync(int pageNumber = 1, int pageSize = 20, string? keyword = null);
        Task<List<MultiSourceTenderDto>> GetDevelopmentAidTendersAsync(string type = "tenders", int page = 1, string? keyword = null);
        Task<List<MultiSourceTenderDto>> GetUnifiedProcurementFeedAsync(string? keyword = null, string? country = null, string? portal = null, int limit = 50);
    }
}
