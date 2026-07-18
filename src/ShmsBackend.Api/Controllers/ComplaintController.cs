using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
}
