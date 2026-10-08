# Zimbabwe Tender API - Authentication Guide

## Quick Start: Getting Your Auth Token

### Step 1: Login
```bash
curl -X POST "https://localhost:7016/api/auth/login" \
  -H "Content-Type: application/json" \
  -d '{
    "email": "admin@tenderapi.com",
    "password": "Admin@123"
  }'
```

**Response:**
```json
{
  "success": true,
  "message": "Login successful",
  "data": {
    "userId": "1",
    "username": "Administrator",
    "email": "admin@tenderapi.com",
    "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "roles": ["Admin", "ProjectManager", "Supervisor"],
    "expiresAt": "2026-01-28T15:00:00Z"
  }
}
```

### Step 2: Copy the Token
Copy the entire `token` value from the response.

### Step 3: Use Token in Swagger UI

1. Click the **Authorize** button (🔒 lock icon) at the top right
2. Enter: `Bearer YOUR_TOKEN_HERE`
   - Example: `Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...`
3. Click **Authorize**, then **Close**
4. Now all API calls will include your token automatically

### Step 4: Use Token in curl/Postman

**curl:**
```bash
curl -X GET "https://localhost:7016/api/Insights/quick?q=IT%20tenders" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "accept: */*"
```

**Postman:**
- Go to **Headers** tab
- Add header: `Authorization` with value `Bearer YOUR_TOKEN_HERE`

---

## Available Users (Default)

| Email | Password | Roles |
|-------|----------|-------|
| admin@tenderapi.com | Admin@123 | Admin, ProjectManager, Supervisor |
| supervisor@tenderapi.com | Supervisor@123 | Supervisor, Employee |
| hr@tenderapi.com | HR@123 | HR, Employee |
| pm@tenderapi.com | PM@123 | ProjectManager, Employee |
| dev@tenderapi.com | Dev@123 | Development, Employee |
| guest@tenderapi.com | Guest@123 | Guest |

---

## Endpoint Authorization Requirements

### Public Endpoints (No Auth Required)
- `POST /api/auth/register`
- `POST /api/auth/login`
- `GET /api/LiveTenders` (list)
- `GET /api/ClosedTenders` (list)
- `GET /api/AwardNotices` (list)
- `GET /api/ProcurementPlansDb` (list)
- `GET /api/LiveTenders/{id}`
- `GET /api/ClosedTenders/{id}`

### Guest Access (Requires Login)
- `GET /api/LiveTenders/search`
- `GET /api/ClosedTenders/search`
- `GET /api/Search/*`

### ProjectManager Access Required
- `POST /api/Insights/analyze`
- `GET /api/Insights/quick`
- `GET /api/Insights/analyze-custom`
- `GET /api/Insights/company/{companyProfile}`
- All `/api/Analytics/*` endpoints

### Admin Access Required
- `GET /api/Settings`
- `PUT /api/Settings/key/{key}`
- `POST /api/Settings`
- `DELETE /api/Settings/key/{key}`
- `POST /api/Settings/bulk`
- `GET /api/auth/users`
- `POST /api/auth/assign-role`
- `DELETE /api/auth/remove-role`

---

## Testing Insights Endpoints (401 Error Fix)

### Problem
```bash
# ❌ This returns 401 Unauthorized
curl -X GET 'https://localhost:7016/api/Insights/quick?q=IT%20tenders' \
  -H 'accept: */*'
```

### Solution
```bash
# ✅ First, login and get token
TOKEN=$(curl -X POST "https://localhost:7016/api/auth/login" \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@tenderapi.com","password":"Admin@123"}' \
  | jq -r '.data.token')

# ✅ Then use the token
curl -X GET 'https://localhost:7016/api/Insights/quick?q=IT%20tenders&includeAnalysis=true' \
  -H "Authorization: Bearer $TOKEN" \
  -H 'accept: */*'
```

---

## PowerShell Examples

### Login and Save Token
```powershell
$loginBody = @{
    email = "admin@tenderapi.com"
    password = "Admin@123"
} | ConvertTo-Json

$response = Invoke-RestMethod -Uri "https://localhost:7016/api/auth/login" `
    -Method POST `
    -ContentType "application/json" `
    -Body $loginBody

$token = $response.data.token
Write-Host "Token: $token"
```

### Use Token in Requests
```powershell
$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
}

# Get quick insights
Invoke-RestMethod -Uri "https://localhost:7016/api/Insights/quick?q=IT%20tenders" `
    -Method GET `
    -Headers $headers

# Analyze custom query
Invoke-RestMethod -Uri "https://localhost:7016/api/Insights/analyze-custom?q=Axis%20Solutions%20IT%20tenders&maxTenders=10" `
    -Method GET `
    -Headers $headers
```

---

## Role Management Endpoints

### Assign Role to User
```bash
curl -X POST "https://localhost:7016/api/auth/assign-role" \
  -H "Authorization: Bearer YOUR_ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "5",
    "role": "ProjectManager"
  }'
```

### Get User's Roles
```bash
curl -X GET "https://localhost:7016/api/auth/users/5/roles" \
  -H "Authorization: Bearer YOUR_ADMIN_TOKEN"
```

### Remove Role from User
```bash
curl -X DELETE "https://localhost:7016/api/auth/remove-role" \
  -H "Authorization: Bearer YOUR_ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "5",
    "role": "Employee"
  }'
```

### List All Users (Admin Only)
```bash
curl -X GET "https://localhost:7016/api/auth/users" \
  -H "Authorization: Bearer YOUR_ADMIN_TOKEN"
```

---

## Troubleshooting

### Error: 401 Unauthorized
**Cause:** Missing or invalid token
**Fix:** 
1. Login to get a fresh token
2. Make sure you include `Bearer ` prefix
3. Check token hasn't expired (default: 24 hours)

### Error: 403 Forbidden
**Cause:** Your role doesn't have access
**Fix:** 
- Insights endpoints require `ProjectManager` role
- Settings endpoints require `Admin` role
- Login with admin@tenderapi.com for full access

### Token Expired
**Fix:** Login again to get a new token
```bash
curl -X POST "https://localhost:7016/api/auth/login" \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@tenderapi.com","password":"Admin@123"}'
```

---

## JWT Token Structure

The token contains:
- User ID
- Username
- Email
- Roles (array)
- Expiration time

You can decode it at [jwt.io](https://jwt.io) to see the claims.

---

## Security Notes

1. **Never share your token** - it's like a password
2. **Tokens expire after 24 hours** (configurable in settings)
3. **Use HTTPS in production** (wss://your-domain.com)
4. **Store tokens securely** (not in code or logs)
5. **Revoke access** by changing user passwords

---

## Next Steps

1. ✅ Login with admin account
2. ✅ Copy the token
3. ✅ Use Authorize button in Swagger
4. ✅ Test Insights endpoints
5. ✅ Manage user roles if needed
