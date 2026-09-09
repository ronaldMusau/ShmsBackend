using System.Threading.Tasks;
using ShmsBackend.Api.Models.Reports;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services.Reports;

public interface IReportRenderer
{
    Task<byte[]> RenderPdfAsync(ReportData data, CompanySettings company);
    Task<byte[]> RenderExcelAsync(ReportData data, CompanySettings company);
    Task<byte[]> RenderWordAsync(ReportData data, CompanySettings company);
}
