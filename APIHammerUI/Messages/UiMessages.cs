using System;

namespace APIHammerUI.Messages;

/// <summary>
/// Message to update UI with request status
/// </summary>
public class UiUpdateMessage : IMessage
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    public int Priority { get; set; } = 100; // High priority for UI updates
    
    /// <summary>
    /// Action to execute on the UI thread
    /// </summary>
    public Action UiAction { get; set; } = null!;
    
    /// <summary>
    /// Optional description of the UI update
    /// </summary>
    public string? Description { get; set; }
}

/// <summary>
/// Message to show notifications to the user
/// </summary>
public class NotificationMessage : IMessage
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    public int Priority { get; set; } = 50;
    
    /// <summary>
    /// Title of the notification
    /// </summary>
    public string Title { get; set; } = "";
    
    /// <summary>
    /// Message content
    /// </summary>
    public string Message { get; set; } = "";
    
    /// <summary>
    /// Whether this is a success notification
    /// </summary>
    public bool IsSuccess { get; set; }
    
    /// <summary>
    /// Optional callback when notification is shown
    /// </summary>
    public Action? OnShown { get; set; }
}