using System.Collections.Generic;
using VrcTwitchOscBridge.Services;
using Xunit;

namespace VrcTwitchOscBridge.Tests;

public sealed class TwitchEventSubSessionTests
{
    [Fact]
    public async Task ListenAsync_ContinuesReceivingFramesWhileNotificationHandlerIsBlocked()
    {
        var frames = new Queue<string?>([
            Notification("notification-1"),
            KeepAlive(),
            Reconnect(),
            Notification("notification-2"),
            null]);
        var reconnectFrameRead = NewSignal();
        var postReconnectNotificationRead = NewSignal();
        var handlerStarted = NewSignal();
        var handlerCompleted = NewSignal();
        var secondHandlerCompleted = NewSignal();
        var releaseHandler = NewSignal();
        var receivedMessageTypes = new List<string>();

        Task<string?> ReceiveAsync(CancellationToken cancellationToken)
        {
            string? frame;
            lock (frames)
            {
                frame = frames.Dequeue();
            }

            var messageType = frame?.Contains("\"message_type\":\"notification\"", StringComparison.Ordinal) == true
                ? "notification"
                : frame?.Contains("\"message_type\":\"session_keepalive\"", StringComparison.Ordinal) == true
                    ? "session_keepalive"
                    : frame?.Contains("\"message_type\":\"session_reconnect\"", StringComparison.Ordinal) == true
                        ? "session_reconnect"
                        : "closed";
            if (messageType != "closed")
            {
                lock (receivedMessageTypes)
                {
                    receivedMessageTypes.Add(messageType);
                }
            }

            if (messageType == "session_reconnect")
            {
                reconnectFrameRead.TrySetResult();
            }
            else if (frame?.Contains("notification-2", StringComparison.Ordinal) == true)
            {
                postReconnectNotificationRead.TrySetResult();
            }

            return Task.FromResult(frame);
        }

        var handledMessageIds = new List<string>();
        await using var session = new TwitchEventSubSession(ReceiveAsync, notificationQueueCapacity: 2);
        var listenTask = session.ListenAsync(async notification =>
        {
            lock (handledMessageIds)
            {
                handledMessageIds.Add(notification.MessageId);
            }

            handlerStarted.TrySetResult();
            try
            {
                await releaseHandler.Task;
            }
            finally
            {
                if (notification.MessageId == "notification-1")
                {
                    handlerCompleted.TrySetResult();
                }
                else
                {
                    secondHandlerCompleted.TrySetResult();
                }
            }
        });

        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await reconnectFrameRead.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var result = await listenTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(result.ReconnectRequested);
        await postReconnectNotificationRead.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(["notification", "session_keepalive", "session_reconnect", "notification"], receivedMessageTypes);

        releaseHandler.TrySetResult();
        await handlerCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await secondHandlerCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(["notification-1", "notification-2"], handledMessageIds);
    }

    [Fact]
    public async Task ListenAsync_PreservesNotificationOrdering()
    {
        var frames = new Queue<string?>([
            Notification("notification-1"),
            Notification("notification-2"),
            Notification("notification-3"),
            null]);
        var handledMessageIds = new List<string>();

        Task<string?> ReceiveAsync(CancellationToken cancellationToken)
        {
            lock (frames)
            {
                return Task.FromResult(frames.Dequeue());
            }
        }

        await using var session = new TwitchEventSubSession(ReceiveAsync, notificationQueueCapacity: 2);
        var result = await session.ListenAsync(notification =>
        {
            lock (handledMessageIds)
            {
                handledMessageIds.Add(notification.MessageId);
            }

            return Task.CompletedTask;
        });

        Assert.False(result.ReconnectRequested);
        Assert.Equal(["notification-1", "notification-2", "notification-3"], handledMessageIds);
    }

    [Fact]
    public async Task ListenAsync_WaitsWhenNotificationQueueIsFull()
    {
        using var sessionCancellation = new CancellationTokenSource();
        var frames = new Queue<string?>([
            Notification("notification-1"),
            Notification("notification-2"),
            Notification("notification-3"),
            Notification("notification-4"),
            Notification("notification-5")]);
        var handlerStarted = NewSignal();
        var fourthFrameRead = NewSignal();
        var fifthFrameRead = NewSignal();
        var receiveCount = 0;

        Task<string?> ReceiveAsync(CancellationToken cancellationToken)
        {
            var callNumber = Interlocked.Increment(ref receiveCount);
            if (callNumber == 5)
            {
                fifthFrameRead.TrySetResult();
            }

            lock (frames)
            {
                var frame = frames.Dequeue();
                if (callNumber == 4)
                {
                    fourthFrameRead.TrySetResult();
                }

                return Task.FromResult(frame);
            }
        }

        await using var session = new TwitchEventSubSession(ReceiveAsync, notificationQueueCapacity: 2);
        var listenTask = session.ListenAsync(async notification =>
        {
            handlerStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, sessionCancellation.Token);
        }, sessionCancellation.Token);

        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await fourthFrameRead.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var fifthFrameObservation = await Task.WhenAny(
            fifthFrameRead.Task,
            Task.Delay(TimeSpan.FromMilliseconds(100)));
        Assert.NotSame(fifthFrameRead.Task, fifthFrameObservation);

        sessionCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => listenTask);
    }

    [Fact]
    public async Task ListenAsync_IsolatesNotificationHandlerExceptions()
    {
        var frames = new Queue<string?>([
            Notification("notification-1"),
            Notification("notification-2"),
            null]);
        var handledMessageIds = new List<string>();
        var shouldThrow = true;

        Task<string?> ReceiveAsync(CancellationToken cancellationToken)
        {
            lock (frames)
            {
                return Task.FromResult(frames.Dequeue());
            }
        }

        await using var session = new TwitchEventSubSession(ReceiveAsync, notificationQueueCapacity: 2);
        var result = await session.ListenAsync(notification =>
        {
            if (shouldThrow)
            {
                shouldThrow = false;
                throw new InvalidOperationException("test handler failure");
            }

            lock (handledMessageIds)
            {
                handledMessageIds.Add(notification.MessageId);
            }

            return Task.CompletedTask;
        });

        Assert.False(result.ReconnectRequested);
        Assert.Equal(["notification-2"], handledMessageIds);
    }

    [Fact]
    public async Task ListenAsync_CancelsWorkerAndDoesNotProcessQueuedNotificationsAfterStop()
    {
        using var sessionCancellation = new CancellationTokenSource();
        var frames = new Queue<string?>([
            Notification("notification-1"),
            Notification("notification-2"),
            Notification("notification-3")]);
        var handlerStarted = NewSignal();
        var receiverBlocked = NewSignal();
        var unexpectedNotification = NewSignal();
        var handledMessageIds = new List<string>();
        var receiveCount = 0;

        Task<string?> ReceiveAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref receiveCount) == 4)
            {
                receiverBlocked.TrySetResult();
                return WaitForCancellationAsync(cancellationToken);
            }

            lock (frames)
            {
                return Task.FromResult(frames.Dequeue());
            }
        }

        static async Task<string?> WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }

        await using var session = new TwitchEventSubSession(ReceiveAsync, notificationQueueCapacity: 2);
        var listenTask = session.ListenAsync(async notification =>
        {
            lock (handledMessageIds)
            {
                handledMessageIds.Add(notification.MessageId);
            }

            if (notification.MessageId != "notification-1")
            {
                unexpectedNotification.TrySetResult();
            }

            handlerStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, sessionCancellation.Token);
        }, sessionCancellation.Token);

        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await receiverBlocked.Task.WaitAsync(TimeSpan.FromSeconds(2));
        sessionCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => listenTask);

        Assert.Equal(["notification-1"], handledMessageIds);
        Assert.False(unexpectedNotification.Task.IsCompleted);
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static string Notification(string messageId) =>
        string.Concat(
            "{\"metadata\":{\"message_type\":\"notification\",\"message_id\":\"",
            messageId,
            "\",\"subscription_type\":\"channel.follow\"},\"payload\":{\"event\":{\"id\":\"",
            messageId,
            "\"}}}");

    private static string KeepAlive() =>
        "{\"metadata\":{\"message_type\":\"session_keepalive\"},\"payload\":{}}";

    private static string Reconnect() =>
        "{\"metadata\":{\"message_type\":\"session_reconnect\"},\"payload\":{\"session\":{\"reconnect_url\":\"wss://eventsub.wss.twitch.tv/ws\"}}}";
}
