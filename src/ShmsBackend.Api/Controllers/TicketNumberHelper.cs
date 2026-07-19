using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;

namespace ShmsBackend.Api.Controllers;

public static class TicketNumberHelper
{
    public static async Task<string> GenerateAsync(ShmsDbContext context, string houseNumber)
    {
        var connection = context.Database.GetDbConnection();
        bool wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed)
        {
            await connection.OpenAsync();
        }
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT NEXT VALUE FOR ComplaintTicketSequence";
        var result = await command.ExecuteScalarAsync();
        long sequenceValue = Convert.ToInt64(result);
        if (wasClosed)
        {
            await connection.CloseAsync();
        }

        var datePart = DateTime.UtcNow.ToString("yyyyMMdd");
        var sequencePart = sequenceValue.ToString("D7");

        return $"{houseNumber}-{datePart}-{sequencePart}";
    }
}
