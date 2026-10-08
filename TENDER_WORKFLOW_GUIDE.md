# Tender Workflow Features - Implementation Guide

## Overview
This document provides a comprehensive guide to the new tender workflow features added to the Zimbabwe Tender API system. These features enable complete tender management including assignments, document uploads, checklists, and approval workflows.

## New Features

### 1. Tender Assignments
**Endpoint Base:** `/api/TenderAssignments`

Allows assigning tenders to team members with email notifications.

#### Key Endpoints:
- **POST** `/api/TenderAssignments/assign` - Assign a tender to a user
- **GET** `/api/TenderAssignments/my-assignments` - Get current user's assignments
- **GET** `/api/TenderAssignments/tender/{tenderId}/{tenderType}` - Get all assignments for a tender
- **PUT** `/api/TenderAssignments/{id}/status` - Update assignment status
- **DELETE** `/api/TenderAssignments/{id}` - Delete an assignment

#### Example: Assign a Tender
```json
POST /api/TenderAssignments/assign
{
  "tenderId": 123,
  "tenderType": "Live",
  "assignedToUserId": 5,
  "dueDate": "2026-02-15T00:00:00",
  "assignmentInstructions": "Please review this tender and prepare technical proposal",
  "notes": "High priority - deadline approaching"
}
```

**Response:** The system will:
- Create the assignment record
- Send an email notification to the assignee
- Return the assignment details including user information

### 2. Tender Document Upload
**Endpoint Base:** `/api/TenderDocuments`

Upload and manage tender-related documents (PRAZ certificates, proposals, etc.).

#### Key Endpoints:
- **POST** `/api/TenderDocuments/upload` - Upload a document
- **GET** `/api/TenderDocuments/tender/{tenderId}/{tenderType}` - Get all documents for a tender
- **GET** `/api/TenderDocuments/{id}/download` - Download a document
- **GET** `/api/TenderDocuments/types` - Get available document types
- **PUT** `/api/TenderDocuments/{id}` - Update document details
- **DELETE** `/api/TenderDocuments/{id}` - Delete a document (Admin/Manager only)

#### Example: Upload a Document
```http
POST /api/TenderDocuments/upload
Content-Type: multipart/form-data

tenderId: 123
tenderType: Live
documentType: PRAZ Certificate
description: Valid PRAZ certificate for 2026
file: [file data]
```

**Supported Document Types:**
- PRAZ Certificate
- Tax Clearance
- Company Registration
- Technical Proposal
- Financial Proposal
- Tender Response
- Supporting Document
- Certificate of Compliance
- Bank Statement
- Reference Letter
- Other

### 3. Tender Checklist
**Endpoint Base:** `/api/TenderChecklist`

Track tender requirements and ensure nothing is missed.

#### Key Endpoints:
- **POST** `/api/TenderChecklist` - Create a checklist item
- **POST** `/api/TenderChecklist/bulk` - Create multiple checklist items at once
- **GET** `/api/TenderChecklist/tender/{tenderId}/{tenderType}` - Get tender checklist
- **GET** `/api/TenderChecklist/templates` - Get predefined checklist templates
- **PUT** `/api/TenderChecklist/{id}/status` - Update item status
- **PUT** `/api/TenderChecklist/{id}` - Update item details
- **DELETE** `/api/TenderChecklist/{id}` - Delete a checklist item

#### Example: Create Checklist Items
```json
POST /api/TenderChecklist/bulk
{
  "tenderId": 123,
  "tenderType": "Live",
  "items": [
    {
      "itemTitle": "PRAZ Certificate Uploaded",
      "itemDescription": "Upload valid PRAZ certificate",
      "isRequired": true,
      "category": "Documentation",
      "dueDate": "2026-02-10T00:00:00"
    },
    {
      "itemTitle": "Technical Proposal Prepared",
      "itemDescription": "Prepare technical proposal addressing all requirements",
      "isRequired": true,
      "category": "Technical"
    }
  ]
}
```

#### Example: Update Checklist Item Status
```json
PUT /api/TenderChecklist/5/status
{
  "status": "Completed",
  "notes": "Certificate uploaded and verified"
}
```

**Available Templates:**
1. **Standard Tender Checklist** - General tender requirements
2. **Construction Tender Checklist** - Specific to construction projects

### 4. Tender Approval Workflow
**Endpoint Base:** `/api/TenderApprovals`

Manage multi-stage approval processes for tenders.

#### Key Endpoints:
- **POST** `/api/TenderApprovals/request` - Request approval for a specific stage
- **POST** `/api/TenderApprovals/workflow` - Create complete multi-stage approval workflow
- **GET** `/api/TenderApprovals/pending` - Get pending approvals for current user
- **GET** `/api/TenderApprovals/my-requests` - Get approval requests made by current user
- **GET** `/api/TenderApprovals/tender/{tenderId}/{tenderType}` - Get all approvals for a tender
- **PUT** `/api/TenderApprovals/{id}/respond` - Approve, reject, or request changes
- **DELETE** `/api/TenderApprovals/{id}` - Delete a pending approval (Admin/Manager only)

#### Example: Create Approval Workflow
```json
POST /api/TenderApprovals/workflow
{
  "tenderId": 123,
  "tenderType": "Live",
  "stages": [
    {
      "stageName": "Initial Review",
      "approverUserId": 3,
      "requestDetails": "Please review tender documents for completeness",
      "isRequired": true
    },
    {
      "stageName": "Technical Review",
      "approverUserId": 5,
      "requestDetails": "Technical evaluation required",
      "isRequired": true
    },
    {
      "stageName": "Final Approval",
      "approverUserId": 2,
      "requestDetails": "Final approval before submission",
      "isRequired": true
    }
  ]
}
```

#### Example: Respond to Approval Request
```json
PUT /api/TenderApprovals/7/respond
{
  "status": "Approved",
  "comments": "All requirements met. Approved for next stage."
}
```

**Approval Statuses:**
- `Pending` - Awaiting review
- `Approved` - Approved by reviewer
- `Rejected` - Rejected with reason
- `RequestChanges` - Changes requested before approval

## Email Notifications

The system automatically sends email notifications for:

1. **Tender Assignments** - When a tender is assigned to a user
2. **Approval Requests** - When an approval is requested
3. **Approval Responses** - When an approval is granted/rejected

### Email Configuration

Update your `appsettings.json` with SMTP settings:

```json
{
  "EmailSettings": {
    "SmtpHost": "smtp.gmail.com",
    "SmtpPort": 587,
    "FromEmail": "noreply@axissolutions.co.zw",
    "FromName": "Zimbabwe Tender API System",
    "Username": "your-email@gmail.com",
    "Password": "your-app-password",
    "EnableSsl": true
  }
}
```

**Note for Gmail:**
- Use an App Password, not your regular Gmail password
- Enable 2-factor authentication on your Google account
- Generate an App Password from Google Account Settings > Security > App Passwords

## Database Schema

### New Tables Created:

1. **TenderAssignments**
   - Tracks tender assignments to users
   - Includes due dates, status, and email tracking

2. **TenderDocuments**
   - Stores uploaded document metadata
   - Links to physical files on the server
   - Tracks document versions

3. **TenderChecklistItems**
   - Individual checklist items for tenders
   - Tracks completion status and responsible users
   - Supports categorization and ordering

4. **TenderApprovals**
   - Approval workflow tracking
   - Sequential approval chain support
   - Links approver, requester, and previous approvals

All tables inherit from `AuditableEntity` providing:
- Created/Updated/Deleted timestamps
- Created/Updated/Deleted by user tracking
- Soft delete support
- Row versioning for concurrency

## Common Workflows

### Workflow 1: Complete Tender Submission Process

1. **Assign Tender to Team Member**
   ```
   POST /api/TenderAssignments/assign
   ```

2. **Create Checklist from Template**
   ```
   GET /api/TenderChecklist/templates
   POST /api/TenderChecklist/bulk
   ```

3. **Upload Required Documents**
   ```
   POST /api/TenderDocuments/upload (multiple times)
   ```

4. **Update Checklist as Items are Completed**
   ```
   PUT /api/TenderChecklist/{id}/status
   ```

5. **Request Approvals**
   ```
   POST /api/TenderApprovals/workflow
   ```

6. **Approvers Review and Approve**
   ```
   PUT /api/TenderApprovals/{id}/respond
   ```

7. **Final Submission**

### Workflow 2: Document Management

1. **Upload Document**
   ```
   POST /api/TenderDocuments/upload
   ```

2. **View All Tender Documents**
   ```
   GET /api/TenderDocuments/tender/{tenderId}/Live
   ```

3. **Download Specific Document**
   ```
   GET /api/TenderDocuments/{id}/download
   ```

4. **Update Document Details**
   ```
   PUT /api/TenderDocuments/{id}
   ```

## Security & Permissions

### Authentication
All endpoints require authentication via JWT token.

### Authorization
- **Standard Users**: Can create assignments, upload documents, manage checklists
- **Approvers**: Can respond to approval requests assigned to them
- **Admin/Manager**: Can delete documents and approvals

## File Storage

Documents are stored in:
```
{ContentRoot}/uploads/tenders/{tenderId}/
```

Files are named with unique GUIDs to prevent conflicts:
```
uploads/tenders/123/a8f3c2e1-4b5d-4e2f-9c1a-2d3e4f5a6b7c.pdf
```

**Maximum Upload Size:** 50 MB per file

## Error Handling

All endpoints return standard HTTP status codes:

- **200 OK** - Successful request
- **201 Created** - Resource created successfully
- **204 No Content** - Successful update/delete
- **400 Bad Request** - Invalid request data
- **401 Unauthorized** - Authentication required
- **403 Forbidden** - Insufficient permissions
- **404 Not Found** - Resource not found
- **500 Internal Server Error** - Server error

## Testing the Features

### 1. Test Tender Assignment
```bash
curl -X POST "https://localhost:8095/api/TenderAssignments/assign" \
  -H "Authorization: Bearer {your-token}" \
  -H "Content-Type: application/json" \
  -d '{
    "tenderId": 1,
    "tenderType": "Live",
    "assignedToUserId": 2,
    "assignmentInstructions": "Test assignment"
  }'
```

### 2. Test Document Upload
```bash
curl -X POST "https://localhost:8095/api/TenderDocuments/upload" \
  -H "Authorization: Bearer {your-token}" \
  -F "tenderId=1" \
  -F "tenderType=Live" \
  -F "documentType=PRAZ Certificate" \
  -F "file=@certificate.pdf"
```

### 3. Test Checklist Creation
```bash
curl -X POST "https://localhost:8095/api/TenderChecklist" \
  -H "Authorization: Bearer {your-token}" \
  -H "Content-Type: application/json" \
  -d '{
    "tenderId": 1,
    "tenderType": "Live",
    "itemTitle": "Review Tender Requirements"
  }'
```

### 4. Test Approval Request
```bash
curl -X POST "https://localhost:8095/api/TenderApprovals/request" \
  -H "Authorization: Bearer {your-token}" \
  -H "Content-Type: application/json" \
  -d '{
    "tenderId": 1,
    "tenderType": "Live",
    "approvalStage": "Technical Review",
    "approverUserId": 3
  }'
```

## Troubleshooting

### Email Not Sending
1. Check SMTP settings in `appsettings.json`
2. Verify Gmail App Password is correct
3. Check server logs for email errors
4. Ensure port 587 is not blocked by firewall

### File Upload Fails
1. Check file size (max 50MB)
2. Verify uploads directory exists and is writable
3. Check available disk space
4. Review server logs for specific errors

### Assignment Not Created
1. Verify tender exists (correct tenderId and tenderType)
2. Verify assigned user exists (correct assignedToUserId)
3. Check authentication token is valid
4. Review server logs for specific errors

## Migration Information

**Migration Name:** `AddTenderWorkflowFeatures`
**Created:** February 4, 2026

To apply this migration:
```bash
dotnet ef database update
```

To rollback this migration:
```bash
dotnet ef database update {PreviousMigrationName}
```

## Summary

You now have a complete tender workflow system with:
- ✅ Tender assignment with email notifications
- ✅ Document upload and management
- ✅ Checklist tracking
- ✅ Multi-stage approval workflow
- ✅ Email notifications at each stage
- ✅ Complete audit trail
- ✅ Soft delete support
- ✅ Role-based access control

The system is production-ready and can be consumed by your frontend application immediately!
