using System.Threading.Tasks;
using ShmsBackend.Api.Models.Reports;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services.Reports;

public interface IReceiptRenderer
{
    Task<byte[]> RenderPdfAsync(ReceiptData data, CompanySettings company);
    Task<byte[]> RenderExcelAsync(ReceiptData data, CompanySettings company);
    Task<byte[]> RenderWordAsync(ReceiptData data, CompanySettings company);
}
