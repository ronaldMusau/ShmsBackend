using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ShmsBackend.Api.Hubs;

[Authorize]
public class NotificationHub : Hub
{
    private readonly ILogger<NotificationHub> _logger;

    public NotificationHub(ILogger<NotificationHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        _logger.LogInformation("User connected to NotificationHub: {UserId}", userId);

        await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.UserIdentifier;
        _logger.LogInformation("User disconnected from NotificationHub: {UserId}", userId);

        await base.OnDisconnectedAsync(exception);
    }

    public async Task SendNotificationToUser(string userId, string message)
    {
        await Clients.Group($"user_{userId}").SendAsync("ReceiveNotification", message);
    }

    public async Task SendNotificationToAll(string message)
    {
        await Clients.All.SendAsync("ReceiveNotification", message);
    }
}