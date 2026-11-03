using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CustomElements;

public sealed class PreviewDelivery
{
    private sealed class Session
    {
        public readonly string nonce = Guid.NewGuid().ToString("N");
        public readonly TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly ConcurrentDictionary<ulong, Session> sessions = new();

    public bool Acknowledge(ulong player, string nonce)
    {
        return sessions.TryGetValue(player, out var session) && session.nonce == nonce &&
            session.ready.TrySetResult(true);
    }

    public Task Start(ulong player, Func<string, Task> send, Action<string> log,
        int attempts = 30, int delayMilliseconds = 2000)
    {
        var session = new Session();
        sessions.AddOrUpdate(player, session, (_, previous) =>
        {
            previous.ready.TrySetResult(false); return session;
        });
        // Return the login response immediately; the client must finish loading before it can ACK.
        return Task.Run(async () =>
        {
            try
            {
                for (int attempt = 0; attempt < attempts && !session.ready.Task.IsCompleted; attempt++)
                {
                    try { await send(session.nonce); }
                    catch (Exception error) { log("send failed: " + error.Message); }
                    await Task.WhenAny(session.ready.Task, Task.Delay(delayMilliseconds));
                }
                bool confirmed = session.ready.Task.IsCompleted && await session.ready.Task;
                log(confirmed ? "client acknowledged preview installation" : "preview installation unconfirmed");
            }
            finally
            {
                ((ICollection<KeyValuePair<ulong, Session>>)sessions).Remove(new(player, session));
            }
        });
    }
}
