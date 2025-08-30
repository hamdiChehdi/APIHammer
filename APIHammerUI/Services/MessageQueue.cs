using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using APIHammerUI.Messages;

namespace APIHammerUI.Services;

/// <summary>
/// Thread-safe message queue with priority support
/// </summary>
public class MessageQueue : IDisposable
{
    private readonly ConcurrentQueue<IMessage> _queue = new();
    private readonly SemaphoreSlim _semaphore = new(0);
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private bool _disposed;

    /// <summary>
    /// Enqueue a message with the specified priority
    /// </summary>
    public void Enqueue(IMessage message)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MessageQueue));

        _queue.Enqueue(message);
        _semaphore.Release();
    }

    /// <summary>
    /// Dequeue the highest priority message
    /// </summary>
    public async Task<IMessage?> DequeueAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return null;

        var combinedToken = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _cancellationTokenSource.Token).Token;

        try
        {
            await _semaphore.WaitAsync(combinedToken);
            
            if (_disposed)
                return null;

            // Get all available messages and sort by priority
            var messages = new List<IMessage>();
            while (_queue.TryDequeue(out var message))
            {
                messages.Add(message);
            }

            if (!messages.Any())
                return null;

            // Sort by priority (highest first), then by creation time (oldest first)
            var sortedMessages = messages
                .OrderByDescending(m => m.Priority)
                .ThenBy(m => m.CreatedAt)
                .ToList();

            // Re-enqueue all except the highest priority one
            var highestPriorityMessage = sortedMessages.First();
            foreach (var msg in sortedMessages.Skip(1))
            {
                _queue.Enqueue(msg);
                _semaphore.Release();
            }

            return highestPriorityMessage;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Get the current queue size
    /// </summary>
    public int Count => _queue.Count;

    /// <summary>
    /// Clear all messages from the queue
    /// </summary>
    public void Clear()
    {
        while (_queue.TryDequeue(out _))
        {
            // Empty the queue
        }
        
        // Reset semaphore
        while (_semaphore.CurrentCount > 0)
        {
            _semaphore.Wait(0);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cancellationTokenSource.Cancel();
        _semaphore.Dispose();
        _cancellationTokenSource.Dispose();
    }
}