using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;

namespace ShmsBackend.Api.Controllers;

public static class TicketNumberHelper
{
    public static async Task<string> GenerateAsync(ShmsDbContext context, string houseNumber)
    {
        var sequenceValue = await context.Database
            .SqlQuery<int>($"SELECT NEXT VALUE FOR ComplaintTicketSequence AS Value").SingleAsync();

        var datePart = DateTime.UtcNow.ToString("yyyyMMdd");
        var sequencePart = sequenceValue.ToString("D7");

        return $"{houseNumber}-{datePart}-{sequencePart}";
    }
}
