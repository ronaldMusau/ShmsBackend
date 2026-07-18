using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ComplaintController : ControllerBase
{
    private readonly ShmsDbContext _context;

    public ComplaintController(ShmsDbContext context)
    {
        _context = context;
    }

    private async Task<string> GenerateTicketNumberAsync(string houseNumber)
    {
        var sequenceValue = await _context.Database
            .SqlQuery<int>($"SELECT NEXT VALUE FOR ComplaintTicketSequence AS Value").SingleAsync();

        var datePart = DateTime.UtcNow.ToString("yyyyMMdd");
        var sequencePart = sequenceValue.ToString("D7");

        return $"{houseNumber}-{datePart}-{sequencePart}";
    }

    // TEMPORARY — remove once the real creation endpoint exists
    [HttpGet("test-ticket-number/{houseNumber}")]
    public async Task<IActionResult> TestTicketNumber(string houseNumber)
    {
        var ticketNumber = await GenerateTicketNumberAsync(houseNumber);
        return Ok(new { success = true, ticketNumber });
    }
}
