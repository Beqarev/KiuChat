using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace KiuChat.Hubs;

public class ChatHub : Hub
{
    private static ConcurrentDictionary<string, string> _waitingUsers = new ConcurrentDictionary<string, string>();
    private static ConcurrentDictionary<string, string> _chattingPairs = new ConcurrentDictionary<string, string>();

    public async Task Register(string nickname)
    {
        string currentUserConnectionId = Context.ConnectionId;
        Console.WriteLine($"User trying to register: {nickname} ({currentUserConnectionId})");

        var waitingPartner = _waitingUsers.FirstOrDefault();

        if (waitingPartner.Key != null && waitingPartner.Key != currentUserConnectionId)
        {
            if (_waitingUsers.TryRemove(waitingPartner.Key, out string partnerNickname))
            {
                string partnerConnectionId = waitingPartner.Key;
                _chattingPairs.TryAdd(currentUserConnectionId, partnerConnectionId);
                _chattingPairs.TryAdd(partnerConnectionId, currentUserConnectionId);

                Console.WriteLine($"Pairing: {nickname} ({currentUserConnectionId}) with {partnerNickname} ({partnerConnectionId})");
                await Clients.Client(currentUserConnectionId).SendAsync("PartnerFound", partnerNickname);
                await Clients.Client(partnerConnectionId).SendAsync("PartnerFound", nickname);
            }
            else
            {
                _waitingUsers.TryAdd(currentUserConnectionId, nickname);
                await Clients.Client(currentUserConnectionId).SendAsync("WaitingForPartner");
                Console.WriteLine($"{nickname} ({currentUserConnectionId}) is now waiting (race condition).");
            }
        }
        else
        {
            _waitingUsers.TryAdd(currentUserConnectionId, nickname);
            await Clients.Client(currentUserConnectionId).SendAsync("WaitingForPartner");
            Console.WriteLine($"{nickname} ({currentUserConnectionId}) is now waiting.");
        }

        Console.WriteLine($"Waiting users: {_waitingUsers.Count}, Chatting pairs: {_chattingPairs.Count / 2}");
    }

    public async Task SendMessage(string message)
    {
        string currentUserConnectionId = Context.ConnectionId;

        if (_chattingPairs.TryGetValue(currentUserConnectionId, out string? partnerConnectionId))
        {
            if (partnerConnectionId != null)
            {
                await Clients.Client(partnerConnectionId).SendAsync("ReceiveMessage", message);
                Console.WriteLine($"Message from {currentUserConnectionId} to {partnerConnectionId}: {message}");
            }
        }
        else
        {
            Console.WriteLine($"SendMessage failed: User {currentUserConnectionId} not found in chatting pairs.");
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        string currentUserConnectionId = Context.ConnectionId;
        Console.WriteLine($"User disconnected: {currentUserConnectionId}");

        _waitingUsers.TryRemove(currentUserConnectionId, out _);

        if (_chattingPairs.TryRemove(currentUserConnectionId, out string? partnerConnectionId))
        {
            if (partnerConnectionId != null)
            {
                Console.WriteLine($"User {currentUserConnectionId} was chatting with {partnerConnectionId}. Notifying partner.");
                await Clients.Client(partnerConnectionId).SendAsync("PartnerLeft");
                _chattingPairs.TryRemove(partnerConnectionId, out _);
            }
        }

        Console.WriteLine($"After disconnect: Waiting users: {_waitingUsers.Count}, Chatting pairs: {_chattingPairs.Count / 2}");
        await base.OnDisconnectedAsync(exception);
    }
}
