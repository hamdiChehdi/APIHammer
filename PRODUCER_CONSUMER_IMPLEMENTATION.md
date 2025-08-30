# Producer/Consumer Pattern with Message Queue Implementation

## Overview

This implementation introduces a robust Producer/Consumer Pattern with Message Queue system to prevent UI freezing during HTTP request processing. The new architecture separates HTTP request processing from the UI thread, providing a responsive user experience.

## Architecture Components

### 1. Message System (`APIHammerUI.Messages`)

#### `IMessage`
Base interface for all messages with common properties:
- `Id`: Unique identifier
- `CreatedAt`: Timestamp
- `Priority`: Message priority for queue ordering

#### `HttpRequestMessage`
Message for requesting HTTP processing:
- Contains `HttpRequest` to process
- Includes `CancellationToken` for cancellation support
- Optional completion callback

#### `HttpRequestResponseMessage`
Message containing HTTP processing results:
- Response data, timing, and size information
- Success/error status
- Links back to original request

#### `UiUpdateMessage`
Message for UI thread operations:
- Contains `Action` to execute on UI thread
- High priority for responsive UI updates

#### `NotificationMessage`
Message for user notifications:
- Title and message content
- Success/error indicator
- Optional callback when shown

### 2. Message Queue System (`APIHammerUI.Services`)

#### `MessageQueue`
Thread-safe priority queue:
- Concurrent enqueue/dequeue operations
- Priority-based message ordering
- Cancellation support
- Memory-efficient implementation

#### `MessageQueueService`
Main orchestrator managing:
- **HTTP Request Processor**: Background thread processing HTTP requests
- **UI Update Processor**: Thread handling UI updates on main thread
- Separate queues for different message types
- Automatic error handling and recovery

#### `ApplicationServiceManager`
Singleton service manager:
- Initializes services with application dispatcher
- Manages service lifecycle
- Thread-safe singleton implementation

## Key Benefits

### 1. **Non-Blocking UI**
- HTTP requests processed in background threads
- UI remains responsive during long-running operations
- Cancel operations don't freeze the interface

### 2. **Prioritized Processing**
- UI updates get higher priority than background tasks
- Critical operations processed first
- Configurable priority levels

### 3. **Robust Error Handling**
- Isolated error handling per request
- Automatic retry capabilities
- Graceful degradation on failures

### 4. **Memory Management**
- Efficient queue implementation
- Automatic cleanup of completed tasks
- Configurable memory limits

### 5. **Scalability**
- Easy to add new message types
- Configurable number of processor threads
- Queue statistics and monitoring

## Usage Examples

### Sending HTTP Request
```csharp
// Create request message
var requestMessage = new HttpRequestMessage
{
    Request = httpRequest,
    CancellationToken = cancellationToken,
    Priority = 0
};

// Queue for processing
ApplicationServiceManager.Instance.MessageQueue.QueueHttpRequest(requestMessage);
```

### UI Updates
```csharp
// Queue UI update
ApplicationServiceManager.Instance.MessageQueue.QueueUiUpdate(new UiUpdateMessage
{
    Priority = 100,
    UiAction = () => {
        // Update UI controls
        httpRequest.Response = "Updated!";
    }
});
```

### Notifications
```csharp
// Queue notification
ApplicationServiceManager.Instance.MessageQueue.QueueNotification(new NotificationMessage
{
    Title = "Request Complete",
    Message = "HTTP request processed successfully",
    IsSuccess = true
});
```

## Configuration

### Queue Priorities
- **100**: High priority (UI updates)
- **50**: Medium priority (notifications)
- **0**: Normal priority (HTTP requests)

### Performance Tuning
- UI update interval: 500ms
- Queue processing: Real-time
- Memory limits: Configurable per message type

## Monitoring

### Queue Statistics
```csharp
var stats = ApplicationServiceManager.Instance.MessageQueue.GetQueueStats();
Console.WriteLine($"HTTP Queue: {stats.HttpRequestQueueSize}");
Console.WriteLine($"UI Queue: {stats.UiUpdateQueueSize}");
```

### Optional Status Monitor
`QueueStatusViewModel` provides real-time queue monitoring:
- Queue sizes
- Processing status
- Performance metrics

## Migration from Previous Implementation

### Before (Blocking)
```csharp
private async void SendButton_Click(object sender, RoutedEventArgs e)
{
    // This would block the UI thread
    var result = await _httpRequestService.SendRequestAsync(httpRequest);
    // UI frozen until completion
}
```

### After (Non-Blocking)
```csharp
private void SendButton_Click(object sender, RoutedEventArgs e)
{
    // Immediate return, no UI blocking
    var requestMessage = new HttpRequestMessage { Request = httpRequest };
    ApplicationServiceManager.Instance.MessageQueue.QueueHttpRequest(requestMessage);
    // UI remains responsive
}
```

## Best Practices

1. **Always use message queue for HTTP operations**
2. **Set appropriate priorities for different message types**
3. **Handle cancellation tokens properly**
4. **Keep UI update actions lightweight**
5. **Monitor queue sizes in production**
6. **Implement proper error handling**

## Extension Points

### Adding New Message Types
1. Implement `IMessage` interface
2. Add processor logic in `MessageQueueService`
3. Create queue methods as needed

### Custom Processors
1. Extend `MessageQueueService`
2. Add new processor threads
3. Implement message routing logic

### Advanced Monitoring
1. Implement metrics collection
2. Add performance counters
3. Create dashboards for queue health

This implementation provides a solid foundation for scalable, responsive HTTP request processing while maintaining clean separation of concerns and excellent user experience.