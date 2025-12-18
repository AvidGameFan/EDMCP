# PowerShell MCP Server Test Script
# Usage: .\test-mcp.ps1 -ServerUrl "ws://localhost:5242/mcp"

param(
    [string]$ServerUrl = "ws://localhost:5242/mcp"
)

Write-Host "=== EDMCP MCP Server Test ===" -ForegroundColor Cyan
Write-Host "Connecting to: $ServerUrl" -ForegroundColor Yellow
Write-Host ""

# Test if server is reachable via HTTP health endpoint
$healthUrl = $ServerUrl -replace "ws://", "http://" -replace "wss://", "https://" -replace "/mcp", "/health"

try {
    Write-Host "Testing health endpoint: $healthUrl" -ForegroundColor Gray
    $health = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing
    Write-Host "? Server is running" -ForegroundColor Green
    Write-Host ""
} catch {
    Write-Host "? Cannot connect to server at $healthUrl" -ForegroundColor Red
    Write-Host "  Make sure the server is running: dotnet run --project EDMCP" -ForegroundColor Yellow
    exit 1
}

Write-Host "To test the WebSocket connection, use one of these tools:" -ForegroundColor Cyan
Write-Host ""
Write-Host "Option 1: websocat (https://github.com/vi/websocat)" -ForegroundColor White
Write-Host "  cargo install websocat" -ForegroundColor Gray
Write-Host "  websocat $ServerUrl" -ForegroundColor Gray
Write-Host ""
Write-Host "Option 2: wscat (https://github.com/hashrocket/wscat)" -ForegroundColor White
Write-Host "  npm install -g wscat" -ForegroundColor Gray
Write-Host "  wscat -c $ServerUrl" -ForegroundColor Gray
Write-Host ""
Write-Host "Then send these JSON-RPC messages:" -ForegroundColor Cyan
Write-Host ""

$messages = @(
    '{"jsonrpc":"2.0","id":"1","method":"initialize","params":{}}',
    '{"jsonrpc":"2.0","id":"2","method":"tools/list"}',
    '{"jsonrpc":"2.0","id":"3","method":"tools/call","params":{"name":"generate_image","arguments":{"prompt":"A beautiful sunset"}}}'
)

foreach ($msg in $messages) {
    Write-Host $msg -ForegroundColor Green
}

Write-Host ""
Write-Host "Example C# client code:" -ForegroundColor Cyan
Write-Host @"
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

var ws = new ClientWebSocket();
await ws.ConnectAsync(new Uri("$ServerUrl"), CancellationToken.None);

var request = new { jsonrpc = "2.0", id = "1", method = "initialize", @params = new { } };
var json = JsonSerializer.Serialize(request);
var bytes = Encoding.UTF8.GetBytes(json);

await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);

var buffer = new byte[1024 * 4];
var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
var response = Encoding.UTF8.GetString(buffer, 0, result.Count);
Console.WriteLine(response);
"@ -ForegroundColor Gray
