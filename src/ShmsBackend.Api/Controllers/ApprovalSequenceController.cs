using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "SuperAdmin,Admin,Secretary")]
public class ApprovalSequenceController : ControllerBase
{
    private readonly ShmsDbContext _context;

    public ApprovalSequenceController(ShmsDbContext context)
    {
        _context = context;
    }

    // GET /api/approvalsequence/{module}
    [HttpGet("{module}")]
    public async Task<IActionResult> GetSequence(string module)
    {
        var steps = await _context.ApprovalSequenceSteps
            .Where(s => s.Module == module)
            .OrderBy(s => s.StepOrder)
            .ToListAsync();
        return Ok(new { success = true, data = steps });
    }

    // PUT /api/approvalsequence/{module}
    [HttpPut("{module}")]
    public async Task<IActionResult> ReplaceSequence(string module, [FromBody] List<Guid> approverIds)
    {
        var existing = await _context.ApprovalSequenceSteps
            .Where(s => s.Module == module)
            .ToListAsync();
        _context.ApprovalSequenceSteps.RemoveRange(existing);

        var newSteps = approverIds.Select((approverId, index) => new ApprovalSequenceStep
        {
            Module = module,
            StepOrder = index + 1,
            ApproverId = approverId,
            CreatedAt = DateTime.UtcNow
        }).ToList();

        await _context.ApprovalSequenceSteps.AddRangeAsync(newSteps);
        await _context.SaveChangesAsync();

        return Ok(new { success = true, data = newSteps });
    }
}
