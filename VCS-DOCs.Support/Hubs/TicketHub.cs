using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace VCS_DOCs.Support.Hubs
{
    [Authorize(Policy = "SupportDeskAccess")]
    public class TicketHub : Hub
    {
        public Task JoinTicket(string ticketId)
            => Groups.AddToGroupAsync(Context.ConnectionId, $"ticket:{ticketId}");

        public Task LeaveTicket(string ticketId)
            => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"ticket:{ticketId}");
    }
}
