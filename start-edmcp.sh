#!/bin/bash
# EDMCP Server Startup Script
# This script is designed to run from the EDMCP release package root directory

export ASPNETCORE_URLS="http://0.0.0.0:5242"
export EASY_DIFFUSION_ADDRESS="${EASY_DIFFUSION_ADDRESS:-http://localhost:9000}"

# Uncomment and modify the line below if Easy Diffusion is on a different machine
# export EASY_DIFFUSION_ADDRESS="http://192.168.1.100:9000"

echo ""
echo "======================================"
echo "   EDMCP MCP Server"
echo "======================================"
echo "Listening on: $ASPNETCORE_URLS"
echo "Easy Diffusion: $EASY_DIFFUSION_ADDRESS"
echo ""

if [ ! -f "EDMCP" ]; then
    echo "ERROR: EDMCP executable not found in current directory!"
    echo ""
    echo "This script should be run from the EDMCP release package directory."
    echo "If you extracted EDMCP_server_linux.tar.gz, run this script from that directory."
    echo ""
    read -p "Press Enter to exit..."
    exit 1
fi

# Make sure EDMCP is executable
chmod +x EDMCP

echo "Starting EDMCP..."
echo ""

./EDMCP

if [ $? -ne 0 ]; then
    echo ""
    echo "ERROR: EDMCP exited with code $?"
    echo ""
    read -p "Press Enter to exit..."
fi
