using System.Collections.Generic;
using System.Threading.Tasks;
using ZimbabweTenderAPI.Models;

namespace ZimbabweTenderAPI.Services
{
    public interface IZambiaTenderScraperService
    {
        /// <summary>
        /// Scrapes a single page of opened tenders from Zambia ZPPA portal.
        /// </summary>
        Task<ZambiaTenderBatch> ScrapeOpenedTendersPageAsync(int pageNumber);

        /// <summary>
        /// Scrapes a range of pages of opened tenders concurrently from Zambia ZPPA portal.
        /// </summary>
        Task<List<ZambiaTender>> ScrapeMultipleOpenedTendersPagesAsync(int startPage, int endPage);

        /// <summary>
        /// Returns total estimated pages available on the Zambia portal.
        /// </summary>
        Task<int> GetTotalPagesAsync();

        /// <summary>
        /// Searches opened tenders by matching keyword across titles, reference numbers, or procuring entities.
        /// </summary>
        Task<ZambiaTenderBatch> SearchOpenedTendersAsync(string keyword, int page = 1, int maxPagesToScan = 5);

        /// <summary>
        /// Filters opened tenders by procuring entity name.
        /// </summary>
        Task<List<ZambiaTender>> GetTendersByEntityAsync(string entityName, int maxPagesToScan = 5);

        /// <summary>
        /// Scrapes published annual procurement plans and downloadable document files from ZPPA.
        /// </summary>
        Task<ZambiaPublicationBatch> ScrapePublishedProcurementPlansAsync(int page = 1, string? cycleId = null);

        /// <summary>
        /// Downloads a remote tender/publication document or PDF with local cache support.
        /// </summary>
        Task<(byte[] FileBytes, string ContentType, string FileName)> DownloadDocumentFileAsync(string fileUrl);

        /// <summary>
        /// Performs AI analysis on a Zambia tender or document content using Gemini AI.
        /// </summary>
        Task<ZambiaDocumentAnalysisResponse> AnalyzeZambiaDocumentAsync(ZambiaDocumentAnalysisRequest request);

        /// <summary>
        /// Scrapes full tender details, metadata, procurement parameters, fees, lots, and attached documents by resource ID.
        /// </summary>
        Task<ZambiaTenderDetailDto> GetTenderDetailsAsync(string resourceId);

        /// <summary>
        /// Scrapes list of attached contract documents, solicitation PDFs, and BOQ spreadsheets for a tender.
        /// </summary>
        Task<List<ZambiaTenderDocumentDto>> GetTenderDocumentsAsync(string resourceId);
    }
}
