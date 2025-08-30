using System;

namespace APIHammerUI.Messages;

/// <summary>
/// Base interface for all messages in the message queue system
/// </summary>
public interface IMessage
{
    /// <summary>
    /// Unique identifier for the message
    /// </summary>
    Guid Id { get; }
    
    /// <summary>
    /// Timestamp when the message was created
    /// </summary>
    DateTime CreatedAt { get; }
    
    /// <summary>
    /// Priority of the message (higher numbers = higher priority)
    /// </summary>
    int Priority { get; }
}