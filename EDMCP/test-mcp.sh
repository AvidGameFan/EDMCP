#!/usr/bin/env bash
# Simple MCP client tester using websocat or wscat
# Usage: ./test-mcp.sh [server_url]
# Example: ./test-mcp.sh ws://localhost:5242/mcp

SERVER_URL="${1:-ws://localhost:5242/mcp}"

echo "=== EDMCP MCP Server Test ==="
echo "Connecting to: $SERVER_URL"
echo ""

# Check if websocat is installed
if command -v websocat &> /dev/null; then
    echo "Using websocat..."
    (
        echo '{"jsonrpc":"2.0","id":"init","method":"initialize","params":{}}'
        sleep 1
        echo '{"jsonrpc":"2.0","id":"list","method":"tools/list"}'
        sleep 1
        echo '{"jsonrpc":"2.0","id":"call","method":"tools/call","params":{"name":"generate_image","arguments":{"prompt":"A beautiful sunset"}}}'
        sleep 5
    ) | websocat "$SERVER_URL"
elif command -v wscat &> /dev/null; then
    echo "Using wscat..."
    wscat -c "$SERVER_URL" << EOF
{"jsonrpc":"2.0","id":"init","method":"initialize","params":{}}
{"jsonrpc":"2.0","id":"list","method":"tools/list"}
{"jsonrpc":"2.0","id":"call","method":"tools/call","params":{"name":"generate_image","arguments":{"prompt":"A beautiful sunset"}}}
EOF
else
    echo "Error: websocat or wscat not found"
    echo "Install one of them to test the MCP server:"
    echo "  - websocat: cargo install websocat"
    echo "  - wscat: npm install -g wscat"
    exit 1
fi

echo ""
echo "Test complete!"
