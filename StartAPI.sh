#!/bin/bash
# Start script for Zimbabwe Tender API on macOS / Linux

DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$DIR/ZimbabweTenderAPI"

echo "====================================="
echo "Zimbabwe Tender API"
echo "====================================="
echo ""
echo "Available Endpoints:"
echo "  - Swagger UI:   http://localhost:8096/swagger"
echo "  - Health Check: http://localhost:8096/health"
echo "  - Hangfire UI:  http://localhost:8096/hangfire"
echo ""
echo "Press Ctrl+C to stop the server"
echo "====================================="
echo ""

# Ensure dotnet is in PATH
export PATH="$PATH:$HOME/.dotnet"

dotnet run
