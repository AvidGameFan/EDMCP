using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EDMCP
{
    public class GenerateImageInput
    {
        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = string.Empty;

        [JsonPropertyName("negative_prompt")]
        public string? NegativePrompt { get; set; }

        [JsonPropertyName("width")]
        public int Width { get; set; } = 1280;

        [JsonPropertyName("height")]
        public int Height { get; set; } = 960;

        [JsonPropertyName("num_outputs")]
        public int NumOutputs { get; set; } = 1;

        [JsonPropertyName("num_inference_steps")]
        public int NumInferenceSteps { get; set; } = 25;

        [JsonPropertyName("guidance_scale")]
        public double GuidanceScale { get; set; } = 7.5;

        [JsonPropertyName("seed")]
        public long Seed { get; set; } = -1;

        [JsonPropertyName("sampler_name")]
        public string SamplerName { get; set; } = "deis";

        [JsonPropertyName("use_stable_diffusion_model")]
        public string UseStableDiffusionModel { get; set; } = Environment.GetEnvironmentVariable("DEFAULT_MODEL") ?? "animagineXL40_v4Opt";
    }

    // JSON-RPC Request/Response types
    public class JsonRpcRequest
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonPropertyName("id")]
        public object? Id { get; set; }  // Changed from string? to object? to handle both string and number

        [JsonPropertyName("method")]
        public string Method { get; set; } = string.Empty;

        [JsonPropertyName("params")]
        public JsonElement? Params { get; set; }
    }

    public class JsonRpcResponse
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonPropertyName("id")]
        public object? Id { get; set; }  // Changed from string? to object? to handle both string and number

        [JsonPropertyName("result")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Result { get; set; }

        [JsonPropertyName("error")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonRpcError? Error { get; set; }
    }

    public class JsonRpcError
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Data { get; set; }
    }

    public class InitializeResult
    {
        [JsonPropertyName("protocolVersion")]
        public string ProtocolVersion { get; set; } = "2024-11-05";

        [JsonPropertyName("capabilities")]
        public object Capabilities { get; set; } = new { tools = new { } };

        [JsonPropertyName("serverInfo")]
        public ServerInfo ServerInfo { get; set; } = new();
    }

    public class ServerInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "EDMCP";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0.0";
    }

    public class Tool
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("inputSchema")]
        public object InputSchema { get; set; } = new { };
    }

    public class ToolListResult
    {
        [JsonPropertyName("tools")]
        public List<Tool> Tools { get; set; } = new();
    }

    public class ToolCallResult
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "text";

        [JsonPropertyName("text")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Text { get; set; }
    }

    public class ImageToolResult
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "image";

        [JsonPropertyName("data")]
        public string Data { get; set; } = string.Empty;  // base64-encoded image data

        [JsonPropertyName("mimeType")]
        public string MimeType { get; set; } = "image/png";
    }

    // MCP standard tool result wrapper
    public class ToolResult
    {
        [JsonPropertyName("content")]
        public List<object> Content { get; set; } = new();

        [JsonPropertyName("isError")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool IsError { get; set; } = false;
    }

    public static class Program
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", policy =>
                {
                    policy.AllowAnyOrigin()
                          .AllowAnyMethod()
                          .AllowAnyHeader();
                });
            });

            var app = builder.Build();

            app.UseCors("AllowAll");
            app.UseWebSockets();

            // HTTP POST endpoint for MCP (used by LM Studio)
            app.MapPost("/mcp", HandleHttpMcp);

            // HTTP GET endpoint for health/info
            app.MapGet("/mcp", async context =>
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    name = "EDMCP",
                    version = "1.0.0",
                    protocols = new[] { "json-rpc" }
                }));
            });

            // WebSocket endpoint for MCP (legacy support)
            app.Map("/mcp/ws", HandleMcpWebSocket);

            // Health check endpoint
            app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

            // Debug endpoint to log all requests
            app.Use(async (context, next) =>
            {
                Console.WriteLine($"[REQUEST] {context.Request.Method} {context.Request.Path} {context.Request.QueryString}");
                Console.WriteLine($"[HEADERS] Content-Type: {context.Request.ContentType}");
                Console.WriteLine($"[HEADERS] Accept: {context.Request.Headers["Accept"]}");

                await next();
            });

            await app.RunAsync();
        }

        private static async Task HandleHttpMcp(HttpContext context)
        {
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

            try
            {
                // Read the body as string first to see what we're receiving
                context.Request.EnableBuffering();
                var bodyText = await new StreamReader(context.Request.Body).ReadToEndAsync();
                context.Request.Body.Position = 0;

                Console.WriteLine($"[DEBUG] Received request body: {bodyText}");
                Console.WriteLine($"[DEBUG] Content-Type: {context.Request.ContentType}");
                Console.WriteLine($"[DEBUG] Method: {context.Request.Method}");

                if (string.IsNullOrWhiteSpace(bodyText))
                {
                    Console.WriteLine("[DEBUG] Body is empty!");
                    context.Response.StatusCode = 400;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new JsonRpcResponse
                    {
                        Error = new JsonRpcError { Code = -32600, Message = "Invalid Request: empty body" }
                    }, options));
                    return;
                }

                JsonRpcRequest? request;
                try
                {
                    request = JsonSerializer.Deserialize<JsonRpcRequest>(bodyText, options);
                }
                catch (JsonException jsonEx)
                {
                    Console.WriteLine($"[DEBUG] JSON deserialization error: {jsonEx.Message}");
                    Console.WriteLine($"[DEBUG] Raw body received: {bodyText}");
                    Console.WriteLine($"[DEBUG] Body length: {bodyText.Length}");
                    Console.WriteLine($"[DEBUG] Body bytes: {string.Join(" ", bodyText.Take(50).Select(c => $"{(int)c:X2}"))}");

                    context.Response.StatusCode = 400;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new JsonRpcResponse
                    {
                        Error = new JsonRpcError
                        {
                            Code = -32700,
                            Message = "Parse error - Invalid JSON",
                            Data = $"Error: {jsonEx.Message}. Body received: {(bodyText.Length > 100 ? bodyText.Substring(0, 100) + "..." : bodyText)}"
                        }
                    }, options));
                    return;
                }

                if (request == null)
                {
                    Console.WriteLine("[DEBUG] Request deserialized to null");
                    context.Response.StatusCode = 400;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new JsonRpcResponse
                    {
                        Error = new JsonRpcError { Code = -32600, Message = "Invalid Request" }
                    }, options));
                    return;
                }

                Console.WriteLine($"[DEBUG] Successfully deserialized request: method={request.Method}, id={request.Id}");

                var response = await HandleMcpRequest(request);
                context.Response.ContentType = "application/json";
                await JsonSerializer.SerializeAsync(context.Response.Body, response, options);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Unhandled exception in HandleHttpMcp: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new JsonRpcResponse
                {
                    Error = new JsonRpcError { Code = -32603, Message = "Internal error", Data = ex.Message }
                }));
            }
        }

        private static async Task HandleMcpWebSocket(HttpContext context)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("WebSocket connection required");
                return;
            }

            using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
            await ProcessMcpMessages(webSocket);
        }

        private static async Task ProcessMcpMessages(WebSocket webSocket)
        {
            var buffer = new byte[1024 * 4];

            try
            {
                while (webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var messageText = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                        var request = JsonSerializer.Deserialize<JsonRpcRequest>(messageText, options);

                        if (request != null)
                        {
                            var response = await HandleMcpRequest(request);
                            var responseJson = JsonSerializer.Serialize(response, JsonOptions);
                            var responseBytes = Encoding.UTF8.GetBytes(responseJson);

                            await webSocket.SendAsync(
                                new ArraySegment<byte>(responseBytes),
                                WebSocketMessageType.Text,
                                true,
                                CancellationToken.None);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WebSocket error: {ex.Message}");
            }
        }

        private static async Task<JsonRpcResponse> HandleMcpRequest(JsonRpcRequest request)
        {
            try
            {
                return request.Method switch
                {
                    "initialize" => new JsonRpcResponse
                    {
                        Id = request.Id,
                        Result = new InitializeResult()
                    },

                    "tools/list" => new JsonRpcResponse
                    {
                        Id = request.Id,
                        Result = new ToolListResult
                        {
                            Tools = new List<Tool>
                            {
                                new Tool
                                {
                                    Name = "generate_image",
                                    Description = "Generate an image using Easy Diffusion based on a text prompt",
                                    InputSchema = new
                                    {
                                        type = "object",
                                        properties = new
                                        {
                                            prompt = new
                                            {
                                                type = "string",
                                                description = "The text prompt describing the image to generate"
                                                //no default allowed - must have a prompt!
                                            },
                                            negative_prompt = new
                                            {
                                                type = "string",
                                                description = "What to avoid in the generated image (optional)",
                                                @default = "worst quality, low quality, low score" //default negative_prompt can be empty string, but not null
                                            },
                                            width = new
                                            {
                                                type = "integer",
                                                description = "Image width in pixels",
                                                @default = 1280
                                            },
                                            height = new
                                            {
                                                type = "integer",
                                                description = "Image height in pixels",
                                                @default = 960
                                            },
                                            num_outputs = new
                                            {
                                                type = "integer",
                                                description = "Number of images to generate",
                                                @default = 1
                                            },
                                            num_inference_steps = new
                                            {
                                                type = "integer",
                                                description = "Number of inference steps",
                                                @default = 25
                                            },
                                            guidance_scale = new
                                            {
                                                type = "number",
                                                description = "Guidance scale for prompt adherence",
                                                @default = 7.5
                                            },
                                            seed = new
                                            {
                                                type = "integer",
                                                description = "Random seed (-1 for random)",
                                                @default = -1
                                            },
                                            sampler_name = new
                                            {
                                                type = "string",
                                                description = "Sampler algorithm",
                                                @default = "deis"
                                            },
                                            use_stable_diffusion_model = new
                                            {
                                                type = "string",
                                                description = "Model to use - specify type (such as SDXL or Flux) or specific model (such as animagineXL40_v4Opt)",
                                                @default = Environment.GetEnvironmentVariable("DEFAULT_MODEL") ?? "animagineXL40_v4Opt"
                                            }
                                        },
                                        required = new[] { "prompt" }
                                    }
                                }
                            }
                        }
                    },

                    "tools/call" => await HandleToolCall(request),

                    _ => new JsonRpcResponse
                    {
                        Id = request.Id,
                        Error = new JsonRpcError
                        {
                            Code = -32601,
                            Message = $"Method not found: {request.Method}"
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                return new JsonRpcResponse
                {
                    Id = request.Id,
                    Error = new JsonRpcError
                    {
                        Code = -32603,
                        Message = "Internal error",
                        Data = ex.Message
                    }
                };
            }
        }

        private static async Task<JsonRpcResponse> HandleToolCall(JsonRpcRequest request)
        {
            if (request.Params == null)
            {
                return new JsonRpcResponse
                {
                    Id = request.Id,
                    Error = new JsonRpcError { Code = -32602, Message = "Missing params" }
                };
            }

            var paramsObj = request.Params.Value;
            var toolName = paramsObj.GetProperty("name").GetString();
            var toolInput = paramsObj.GetProperty("arguments");

            if (toolName != "generate_image")
            {
                return new JsonRpcResponse
                {
                    Id = request.Id,
                    Error = new JsonRpcError { Code = -32602, Message = $"Unknown tool: {toolName}" }
                };
            }

            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var input = JsonSerializer.Deserialize<GenerateImageInput>(toolInput.GetRawText(), options);
            if (input == null)
            {
                return new JsonRpcResponse
                {
                    Id = request.Id,
                    Error = new JsonRpcError { Code = -32602, Message = "Invalid tool input" }
                };
            }

            // Fill in defaults for any null/empty optional parameters
            if (string.IsNullOrEmpty(input.NegativePrompt))
            {
                input.NegativePrompt = "worst quality, low quality, low score";  // Needs to be a string (even if empty string) rather than null
            }

            Console.WriteLine($"[DEBUG] Tool call parameters: prompt='{input.Prompt}', width={input.Width}, height={input.Height}, steps={input.NumInferenceSteps}");

            try
            {
                var images = await GenerateImages(input);

                // Return the images in MCP format
                // If we have images, return them; otherwise return a text result
                if (images.Length > 0)
                {
                    // Return the first image as the primary result
                    var imageData = images[0];

                    // Extract just the base64 data if it's a data URL
                    // Data URLs look like: "data:image/png;base64,iVBORw0KGgo..."
                    var base64Data = imageData;
                    if (imageData.StartsWith("data:"))
                    {
                        // Extract the part after "base64,"
                        var parts = imageData.Split(",", 2);
                        if (parts.Length == 2)
                        {
                            base64Data = parts[1];
                            Console.WriteLine($"[DEBUG] Extracted base64 from data URL");
                        }
                    }

                    Console.WriteLine($"[DEBUG] Image data length: {base64Data.Length} bytes (base64)");

                    // The image data from Easy Diffusion is base64-encoded
                    // Return it in the MCP image format wrapped in content array
                    var toolResult = new ToolResult
                    {
                        Content = new List<object>
                        {
                            new ImageToolResult
                            {
                                Type = "image",
                                Data = base64Data,  // Just the base64 bytes, no header
                                MimeType = "image/png"
                            }
                        },
                        IsError = false
                    };

                    return new JsonRpcResponse
                    {
                        Id = request.Id,
                        Result = toolResult
                    };
                }
                else
                {
                    // No images generated - return text result
                    var toolResult = new ToolResult
                    {
                        Content = new List<object>
                        {
                            new ToolCallResult
                            {
                                Type = "text",
                                Text = "No images generated"
                            }
                        },
                        IsError = false
                    };

                    return new JsonRpcResponse
                    {
                        Id = request.Id,
                        Result = toolResult
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] Tool call error: {ex.Message}");

                var errorResult = new ToolResult
                {
                    Content = new List<object>
                    {
                        new ToolCallResult
                        {
                            Type = "text",
                            Text = $"Image generation failed: {ex.Message}"
                        }
                    },
                    IsError = true
                };

                return new JsonRpcResponse
                {
                    Id = request.Id,
                    Result = errorResult
                };
            }
        }

        private static async Task<string[]> GenerateImages(GenerateImageInput input)
        {
            //Console.WriteLine($"[DEBUG] Environment.GetEnvironmentVariable(\"EASY_DIFFUSION_ADDRESS\"): {Environment.GetEnvironmentVariable("EASY_DIFFUSION_ADDRESS")} ");
            var easyDiffusionAddress = Environment.GetEnvironmentVariable("EASY_DIFFUSION_ADDRESS") ?? "http://localhost:9000";
            if (!easyDiffusionAddress.StartsWith("http"))
                easyDiffusionAddress = "http://" + easyDiffusionAddress;

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(300) };

            // Ensure negative_prompt is never null or "none"
            var negativePrompt = input.NegativePrompt ?? string.Empty;
            if (negativePrompt == "none")
            {
                negativePrompt = string.Empty;
            }

            var payload = new Dictionary<string, object?>
            {
                ["prompt"] = input.Prompt,
                ["negative_prompt"] = negativePrompt,  // Use our cleaned value
                ["width"] = input.Width,
                ["height"] = input.Height,
                ["num_outputs"] = input.NumOutputs,
                ["num_inference_steps"] = input.NumInferenceSteps,
                ["guidance_scale"] = input.GuidanceScale,
                ["seed"] = input.Seed == -1 ? 1 : input.Seed,
                ["used_random_seed"] = input.Seed == -1,
                ["sampler_name"] = input.SamplerName,
                ["scheduler_name"] = "simple",
                ["use_stable_diffusion_model"] = input.UseStableDiffusionModel,
                ["use_vae_model"] = string.Empty,
                ["clip_skip"] = false,
                ["enable_vae_tiling"] = true,
                ["vram_usage_level"] = "low",
                ["output_format"] = "png",
                ["output_quality"] = 75,
                ["output_lossless"] = false,
                ["stream_progress_updates"] = true,
                ["stream_image_progress"] = false,
                ["show_only_filtered_image"] = true,
                ["block_nsfw"] = false,
                ["metadata_output_format"] = "none",
                ["session_id"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
            };

            //Adjust some of the settings for different models, etc.
            payload["guidance_scale"] = input.GuidanceScale > 7.5 ? 7.5 : input.GuidanceScale;
            //Most SDXL anime models use clip_skip. 
            if (input.UseStableDiffusionModel.Contains("animagineXL", StringComparison.InvariantCultureIgnoreCase)
                || input.UseStableDiffusionModel.Contains("pony", StringComparison.InvariantCultureIgnoreCase)
                || input.UseStableDiffusionModel.Contains("illustrious", StringComparison.InvariantCultureIgnoreCase))
            {
                payload["clip_skip"] = true;
            }
            if (input.UseStableDiffusionModel.Contains("flash", StringComparison.InvariantCultureIgnoreCase)
                || input.UseStableDiffusionModel.Contains("turbo", StringComparison.InvariantCultureIgnoreCase)
                || input.UseStableDiffusionModel.Contains("schnell", StringComparison.InvariantCultureIgnoreCase)
                || input.UseStableDiffusionModel.Contains("lightning", StringComparison.InvariantCultureIgnoreCase))

            {
                payload["num_inference_steps"] = input.NumInferenceSteps > 12 ? 12 : input.NumInferenceSteps;  //Don't need so many steps for flash/turbo models
            }
            //Some models have the clip & text encoders baked-in, but if we're not going to allow an option, we have to make some assumptions.
            if (input.UseStableDiffusionModel.Contains("flux", StringComparison.InvariantCultureIgnoreCase))
            {
                payload["use_vae_model"] = "ae";
                payload["guidance_scale"] = 1;
                payload["use_text_encoder_model"] = "['clip_l', 't5xxl_fp16']";
            }
            if (input.UseStableDiffusionModel.Contains("chroma", StringComparison.InvariantCultureIgnoreCase))
            {
                payload["use_vae_model"] = "ae";
                if (input.UseStableDiffusionModel.Contains("flash", StringComparison.InvariantCultureIgnoreCase))
                {
                    payload["guidance_scale"] = 1;
                }
                else
                {
                    payload["guidance_scale"] = 4;
                }
                payload["use_text_encoder_model"] = "t5xxl_fp16";
            }
            //if a model type is specified in the model name, map to a specific one
            switch (payload["use_stable_diffusion_model"]?.ToString()?.ToLower())
            {
                case "anime":
                    payload["use_stable_diffusion_model"] = "animagineXL_v4Opt";
                    break;
                //case string s when s.Contains("illustrious"):
                case "sdxl":
                    payload["use_stable_diffusion_model"] = "sd_xl_base_1.0_0.9vae";
                    break;
                case "sd":
                    payload["use_stable_diffusion_model"] = "sd-v1-5";
                    break;
                case "flux":
                    payload["use_stable_diffusion_model"] = "flux1-dev-bnb-nf4-v2";
                    break;
                case "chroma":
                    payload["use_stable_diffusion_model"] = "Chroma1-HD-Q6_K";
                    break;
            }

            Console.WriteLine($"[DEBUG] Sending to Easy Diffusion: prompt='{payload["prompt"]}', negative_prompt='{payload["negative_prompt"]}'");

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await http.PostAsync($"{easyDiffusionAddress.TrimEnd('/')}/render", content);
            var respText = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                throw new Exception($"Easy Diffusion returned HTTP {resp.StatusCode}: {respText}");
            }

            using var doc = JsonDocument.Parse(respText);
            var root = doc.RootElement;

            var images = new List<string>();

            if (root.TryGetProperty("images", out var imagesElem))
            {
                foreach (var item in imagesElem.EnumerateArray())
                {
                    images.Add(item.GetString() ?? string.Empty);
                }
                return images.ToArray();
            }

            if (root.TryGetProperty("stream", out var streamElem))
            {
                var streamUrl = streamElem.GetString() ?? string.Empty;
                if (streamUrl.StartsWith('/')) 
                    streamUrl = easyDiffusionAddress.TrimEnd('/') + streamUrl;
                
                images = await PollStreamForImages(http, streamUrl);
                return images.ToArray();
            }

            if (root.TryGetProperty("output", out var outputElem))
            {
                foreach (var item in outputElem.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("data", out var d))
                    {
                        images.Add(d.GetString() ?? string.Empty);
                    }
                    else if (item.ValueKind == JsonValueKind.String)
                    {
                        images.Add(item.GetString() ?? string.Empty);
                    }
                }
                return images.ToArray();
            }

            throw new Exception("No images in Easy Diffusion response");
        }

        private static async Task<List<string>> PollStreamForImages(HttpClient http, string streamUrl)
        {
            var images = new List<string>();
            var attempts = 0;
            var start = DateTime.UtcNow;
            var timeout = 300;  // 5 minutes
            var maxAttempts = 300;
            var pollInterval = 1.0;  // 1 second

            Console.WriteLine($"[DEBUG] Starting to poll stream URL: {streamUrl}");
            Console.WriteLine($"[DEBUG] Poll settings - Interval: {pollInterval}s, Max attempts: {maxAttempts}, Timeout: {timeout}s");

            while (true)
            {
                attempts++;
                
                // Check for timeout
                var elapsedSeconds = (DateTime.UtcNow - start).TotalSeconds;
                if (elapsedSeconds > timeout)
                {
                    throw new Exception($"Generation timed out after {elapsedSeconds:F1}s");
                }

                // Check for max poll attempts
                if (attempts > maxAttempts)
                {
                    throw new Exception($"Maximum polling attempts reached ({maxAttempts})");
                }

                try
                {
                    Console.WriteLine($"[DEBUG] Poll attempt {attempts} at {elapsedSeconds:F1}s");
                    
                    using var resp = await http.GetAsync(streamUrl);
                    var text = await resp.Content.ReadAsStringAsync();

                    Console.WriteLine($"[DEBUG] Response status: {resp.StatusCode}, body length: {text.Length}");

                    // Skip empty responses - generation hasn't started yet
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        Console.WriteLine($"[DEBUG] Empty response, waiting {pollInterval}s...");
                        await Task.Delay(TimeSpan.FromSeconds(pollInterval));
                        continue;
                    }

                    // Parse JSON from response
                    // Easy Diffusion may return multiple JSON objects (one per line)
                    JsonDocument? doc = null;
                    var lines = text.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    
                    if (lines.Length == 0)
                    {
                        Console.WriteLine($"[DEBUG] No lines in response, waiting {pollInterval}s...");
                        await Task.Delay(TimeSpan.FromSeconds(pollInterval));
                        continue;
                    }

                    // Try to parse the last line first (most recent update)
                    for (int i = lines.Length - 1; i >= 0; i--)
                    {
                        try
                        {
                            doc = JsonDocument.Parse(lines[i]);
                            Console.WriteLine($"[DEBUG] Parsed JSON from line {i}");
                            break;
                        }
                        catch (JsonException ex)
                        {
                            Console.WriteLine($"[DEBUG] Failed to parse line {i}: {ex.Message}");
                            if (i == 0)
                            {
                                // Couldn't parse any line
                                await Task.Delay(TimeSpan.FromSeconds(pollInterval));
                                continue;
                            }
                        }
                    }

                    if (doc == null)
                    {
                        Console.WriteLine($"[DEBUG] Could not parse any JSON, waiting {pollInterval}s...");
                        await Task.Delay(TimeSpan.FromSeconds(pollInterval));
                        continue;
                    }

                    var root = doc.RootElement;

                    // Check for status field
                    if (root.TryGetProperty("status", out var statusElem))
                    {
                        var status = statusElem.GetString();
                        Console.WriteLine($"[DEBUG] Status: {status}");

                        if (status == "succeeded")
                        {
                            Console.WriteLine($"[DEBUG] Generation succeeded! Processing output...");
                            
                            // Prefer 'output' format first (Easy Diffusion standard)
                            if (root.TryGetProperty("output", out var outputElem))
                            {
                                Console.WriteLine($"[DEBUG] Found 'output' field");
                                foreach (var item in outputElem.EnumerateArray())
                                {
                                    if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("data", out var d))
                                    {
                                        var imageStr = d.GetString() ?? string.Empty;
                                        images.Add(imageStr);
                                        Console.WriteLine($"[DEBUG] Added image from output object");
                                    }
                                    else if (item.ValueKind == JsonValueKind.String)
                                    {
                                        images.Add(item.GetString() ?? string.Empty);
                                        Console.WriteLine($"[DEBUG] Added image from output string");
                                    }
                                }
                                
                                if (images.Count > 0)
                                {
                                    Console.WriteLine($"[DEBUG] Returning {images.Count} image(s)");
                                    return images;
                                }
                            }

                            // Fallback to 'images' field
                            if (root.TryGetProperty("images", out var imagesElem))
                            {
                                Console.WriteLine($"[DEBUG] Found 'images' field");
                                foreach (var item in imagesElem.EnumerateArray())
                                {
                                    images.Add(item.GetString() ?? string.Empty);
                                    Console.WriteLine($"[DEBUG] Added image from images array");
                                }
                                
                                if (images.Count > 0)
                                {
                                    Console.WriteLine($"[DEBUG] Returning {images.Count} image(s)");
                                    return images;
                                }
                            }

                            // Succeeded but no images - return empty
                            Console.WriteLine($"[DEBUG] Generation succeeded but no images found");
                            return images;
                        }

                        if (status == "failed")
                        {
                            var errorMsg = "Unknown error";
                            if (root.TryGetProperty("detail", out var errElem))
                            {
                                errorMsg = errElem.GetString() ?? "Unknown error";
                            }
                            throw new Exception($"Generation failed: {errorMsg}");
                        }

                        // Check for progress updates
                        if (root.TryGetProperty("step", out var stepElem) && root.TryGetProperty("total_steps", out var totalElem))
                        {
                            var step = stepElem.GetInt32();
                            var totalSteps = totalElem.GetInt32();
                            Console.WriteLine($"[DEBUG] Progress: Step {step}/{totalSteps}");
                        }
                        else if (root.TryGetProperty("step", out var stepOnlyElem))
                        {
                            Console.WriteLine($"[DEBUG] Step: {stepOnlyElem.GetInt32()}");
                        }
                    }

                    // Not finished yet, wait and poll again
                    Console.WriteLine($"[DEBUG] Generation in progress, waiting {pollInterval}s before next poll...");
                    await Task.Delay(TimeSpan.FromSeconds(pollInterval));
                }
                catch (Exception ex) when (!(ex.Message.Contains("timed out") || ex.Message.Contains("failed") || ex.Message.Contains("Maximum")))
                {
                    // Log the error but continue polling
                    Console.WriteLine($"[DEBUG] Error during polling (will retry): {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(pollInterval));
                }
            }
        }
    }
}
