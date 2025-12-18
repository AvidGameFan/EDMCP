# EDMCP - Easy Diffusion Model Context Protocol Server

A C# MCP (Model Context Protocol) server that bridges LM Studio with Easy Diffusion for image generation.

## Features

- **MCP-compliant** JSON-RPC over HTTP (and WebSocket)
- **generate_image** tool for text-to-image generation
- Supports Easy Diffusion API configuration via environment variables
- Polling support for streaming generation responses
- Works with LM Studio and other MCP-compatible tools

## Running the Server

### Prerequisites

1. **.NET 10** runtime installed
2. **Easy Diffusion** running (default: `http://localhost:9000`)

### Start the Server

```bash
dotnet run --project EDMCP
```

The server will listen on `http://0.0.0.0:5242` by default.

### Configure Easy Diffusion Address

If Easy Diffusion is running on a different machine:

```bash
# Windows
set EASY_DIFFUSION_ADDRESS=http://192.168.1.100:9000
dotnet run --project EDMCP

# Linux/macOS
export EASY_DIFFUSION_ADDRESS=http://192.168.1.100:9000
dotnet run --project EDMCP
```

## MCP Protocol

### Endpoints

- **HTTP POST (Primary)**: `http://localhost:5242/mcp`
- **WebSocket**: `ws://localhost:5242/mcp/ws`
- **Health Check**: `GET http://localhost:5242/health`

### Supported Methods

#### initialize

Initializes the MCP server connection.

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": "1",
  "method": "initialize",
  "params": {}
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": "1",
  "result": {
    "protocolVersion": "2024-11-05",
    "capabilities": {
      "tools": {}
    },
    "serverInfo": {
      "name": "EDMCP",
      "version": "1.0.0"
    }
  }
}
```

#### tools/list

Lists all available tools.

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": "2",
  "method": "tools/list"
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": "2",
  "result": {
    "tools": [
      {
        "name": "generate_image",
        "description": "Generate an image using Easy Diffusion based on a text prompt",
        "inputSchema": { /* JSON schema */ }
      }
    ]
  }
}
```

#### tools/call

Calls the generate_image tool to create images.

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": "3",
  "method": "tools/call",
  "params": {
    "name": "generate_image",
    "arguments": {
      "prompt": "A beautiful sunset over mountains",
      "negative_prompt": "blurry, low quality",
      "width": 1024,
      "height": 768,
      "num_outputs": 1,
      "num_inference_steps": 30,
      "guidance_scale": 7.5,
      "seed": -1,
      "sampler_name": "deis",
      "use_stable_diffusion_model": "animagineXL40_v4Opt"
    }
  }
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": "3",
  "result": {
    "type": "text",
    "text": "Generated 1 image(s)"
  }
}
```

## Tool: generate_image

### Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `prompt` | string | (required) | Text description of image to generate |
| `negative_prompt` | string | null | What to avoid in the image |
| `width` | integer | 1280 | Image width in pixels |
| `height` | integer | 960 | Image height in pixels |
| `num_outputs` | integer | 1 | Number of images to generate |
| `num_inference_steps` | integer | 25 | Number of diffusion steps |
| `guidance_scale` | number | 7.5 | Strength of prompt adherence |
| `seed` | integer | -1 | Random seed (-1 for random) |
| `sampler_name` | string | "deis" | Sampling algorithm |
| `use_stable_diffusion_model` | string | "animagineXL40_v4Opt" | Model to use |

## Integration with LM Studio

### Setup Steps

1. **Start the EDMCP server** (this application)
   ```bash
   dotnet run --project EDMCP
   ```

2. **Open LM Studio** and go to **Settings** ? **MCP Servers**

3. **Add new MCP Server** with these settings:
   - **Name**: `EDMCP`
   - **Type**: `HTTP`
   - **URL**: `http://localhost:5242/mcp`
   (Or use your server's IP address if running on another machine)

4. **Click Connect** - LM Studio will:
   - Send `initialize` request
   - Fetch tools via `tools/list`
   - Display `generate_image` tool in the UI

5. **Use the tool** - When the LLM calls the tool, EDMCP will generate images and return results

## Testing

### Quick Health Check
```bash
curl http://localhost:5242/health
# Response: {"status":"ok"}
```

### HTTP Testing with curl
```bash
# Initialize
curl -X POST http://localhost:5242/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":"1","method":"initialize","params":{}}'

# List tools
curl -X POST http://localhost:5242/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":"2","method":"tools/list"}'

# Generate image
curl -X POST http://localhost:5242/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":"3","method":"tools/call","params":{"name":"generate_image","arguments":{"prompt":"A beautiful sunset"}}}'
```

### WebSocket Testing
```bash
# Using websocat (https://github.com/vi/websocat)
websocat ws://localhost:5242/mcp/ws

# Then send JSON-RPC messages as shown in the HTTP examples above
```

Or use the provided test scripts:
```bash
# Linux/macOS
./test-mcp.sh

# Windows PowerShell
.\test-mcp.ps1
```

## Error Handling

JSON-RPC errors follow the standard format:
```json
{
  "jsonrpc": "2.0",
  "id": "1",
  "error": {
    "code": -32603,
    "message": "Internal error",
    "data": "Additional error details"
  }
}
```

Common error codes:
- `-32601` - Method not found
- `-32602` - Invalid params
- `-32603` - Internal error

## Performance Considerations

1. **HTTP Persistence** - Each request is independent
2. **Streaming Polling** - Configurable intervals (1s default) while waiting for image generation
3. **Timeout Protection** - 300s default timeout for generation, configurable
4. **Memory Efficiency** - Streams responses instead of buffering entire images

## Architecture

- **HTTP Handler** - Accepts POST requests with JSON-RPC messages
- **WebSocket Handler** - Accepts WebSocket connections for persistent sessions
- **MCP Request Router** - Dispatches to appropriate handler based on method name
- **Image Generation** - Calls Easy Diffusion REST API and handles streaming responses
- **Stream Polling** - Polls stream URLs for generation completion with configurable intervals

## Troubleshooting

**Easy Diffusion connection failed:**
- Verify Easy Diffusion is running on the configured address
- Check `EASY_DIFFUSION_ADDRESS` environment variable
- Ensure network connectivity between EDMCP and Easy Diffusion

**LM Studio can't connect:**
- Ensure EDMCP server is running
- Check firewall settings
- Verify correct HTTP URL format (`http://` not `https://`)
- If on different machines, use the server's IP address instead of `localhost`

**"unknown scheme" error in LM Studio:**
- Make sure you're using the HTTP endpoint: `http://localhost:5242/mcp`
- Not WebSocket: `ws://localhost:5242/mcp/ws` (that's for older MCP clients)

**Image generation timeout:**
- Increase timeout for long generation tasks
- Check Easy Diffusion logs for errors
- Verify GPU availability on Easy Diffusion machine
