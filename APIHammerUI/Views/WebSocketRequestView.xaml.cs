using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using APIHammerUI.Models;

namespace APIHammerUI.Views;

public partial class WebSocketRequestView : UserControl
{
    private ClientWebSocket? webSocket;
    private CancellationTokenSource? cancellationTokenSource;

    public WebSocketRequestView()
    {
        InitializeComponent();
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WebSocketRequest wsRequest)
            return;

        if (wsRequest.IsConnected)
        {
            await DisconnectWebSocket(wsRequest);
        }
        else
        {
            await ConnectWebSocket(wsRequest);
        }
    }

    private async Task ConnectWebSocket(WebSocketRequest wsRequest)
    {
        try
        {
            wsRequest.IsConnecting = true;
            
            webSocket = new ClientWebSocket();
            cancellationTokenSource = new CancellationTokenSource();

            if (!Uri.TryCreate(wsRequest.Url, UriKind.Absolute, out var uri))
            {
                wsRequest.Messages += $"[{DateTime.Now:HH:mm:ss}] Error: Invalid WebSocket URL\n";
                return;
            }

            await webSocket.ConnectAsync(uri, cancellationTokenSource.Token);
            wsRequest.IsConnected = true;
            wsRequest.Messages += $"[{DateTime.Now:HH:mm:ss}] Connected to {wsRequest.Url}\n";

            // Start listening for messages
            _ = Task.Run(() => ListenForMessages(wsRequest));
        }
        catch (Exception ex)
        {
            wsRequest.Messages += $"[{DateTime.Now:HH:mm:ss}] Connection error: {ex.Message}\n";
        }
        finally
        {
            wsRequest.IsConnecting = false;
        }
    }

    private async Task DisconnectWebSocket(WebSocketRequest wsRequest)
    {
        try
        {
            cancellationTokenSource?.Cancel();
            
            if (webSocket?.State == WebSocketState.Open)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }

            wsRequest.IsConnected = false;
            wsRequest.Messages += $"[{DateTime.Now:HH:mm:ss}] Disconnected\n";
        }
        catch (Exception ex)
        {
            wsRequest.Messages += $"[{DateTime.Now:HH:mm:ss}] Disconnect error: {ex.Message}\n";
        }
        finally
        {
            webSocket?.Dispose();
            webSocket = null;
            cancellationTokenSource?.Dispose();
            cancellationTokenSource = null;
        }
    }

    private async Task ListenForMessages(WebSocketRequest wsRequest)
    {
        if (webSocket == null || cancellationTokenSource == null)
            return;

        var buffer = new byte[1024 * 4];

        try
        {
            while (webSocket.State == WebSocketState.Open && !cancellationTokenSource.Token.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationTokenSource.Token);
                
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    
                    Dispatcher.Invoke(() =>
                    {
                        wsRequest.Messages += $"[{DateTime.Now:HH:mm:ss}] Received: {message}\n";
                    });
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    Dispatcher.Invoke(() =>
                    {
                        wsRequest.IsConnected = false;
                        wsRequest.Messages += $"[{DateTime.Now:HH:mm:ss}] Connection closed by server\n";
                    });
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                wsRequest.Messages += $"[{DateTime.Now:HH:mm:ss}] Receive error: {ex.Message}\n";
                wsRequest.IsConnected = false;
            });
        }
    }

    private async void SendMessageButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WebSocketRequest wsRequest || webSocket?.State != WebSocketState.Open)
            return;

        try
        {
            if (string.IsNullOrWhiteSpace(wsRequest.Message))
                return;

            var messageBytes = Encoding.UTF8.GetBytes(wsRequest.Message);
            await webSocket.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None);
            
            wsRequest.Messages += $"[{DateTime.Now:HH:mm:ss}] Sent: {wsRequest.Message}\n";
            wsRequest.Message = "";
        }
        catch (Exception ex)
        {
            wsRequest.Messages += $"[{DateTime.Now:HH:mm:ss}] Send error: {ex.Message}\n";
        }
    }

    private void UserControl_Unloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is WebSocketRequest wsRequest && wsRequest.IsConnected)
        {
            _ = DisconnectWebSocket(wsRequest);
        }
    }
}