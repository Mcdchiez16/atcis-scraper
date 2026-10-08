# Quick Start Script for Zimbabwe Tender API
# This script stops any running instance and starts the API fresh

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "Zimbabwe Tender API - Quick Restart" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

# Stop any running dotnet processes for this project
Write-Host "Stopping any running instances..." -ForegroundColor Yellow
Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -like "*ZimbabweTenderAPI*"
} | Stop-Process -Force -ErrorAction SilentlyContinue

Start-Sleep -Seconds 2

# Navigate to project directory
$projectPath = "c:\Users\tnyah\Documents\PROJECTS\Axis Solutions\INTERGRATIONS\ZimbabweTenderAPI - v6 database\ZimbabweTenderAPI"
Set-Location $projectPath

Write-Host "Starting Zimbabwe Tender API..." -ForegroundColor Green
Write-Host ""
Write-Host "Available Endpoints:" -ForegroundColor Cyan
Write-Host "  - Swagger UI: http://localhost:8096/swagger" -ForegroundColor White
Write-Host "  - Database APIs" -ForegroundColor Gray
Write-Host "  - EGP Scraping APIs" -ForegroundColor Gray
Write-Host "  - Authentication & User Management" -ForegroundColor Gray
Write-Host "  - System & Management" -ForegroundColor Gray
Write-Host "  - Tender Workflow & Documents (NEW!)" -ForegroundColor Green
Write-Host ""
Write-Host "New Workflow Endpoints:" -ForegroundColor Yellow
Write-Host "  /api/TenderAssignments - Assign tenders to users" -ForegroundColor White
Write-Host "  /api/TenderDocuments - Upload/manage documents" -ForegroundColor White
Write-Host "  /api/TenderChecklist - Manage tender checklists" -ForegroundColor White
Write-Host "  /api/TenderApprovals - Approval workflows" -ForegroundColor White
Write-Host ""
Write-Host "Press Ctrl+C to stop the server" -ForegroundColor Red
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

# Start the application
dotnet run
