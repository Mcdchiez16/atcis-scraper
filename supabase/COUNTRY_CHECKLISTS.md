# Country checklist templates

Both checklist and reference-document migrations were applied to the configured
Supabase project `pqqymbdbkwltzydymild` on 2026-09-08. The REST schema cache was
refreshed and all four tables were confirmed available. The instructions below
remain relevant when setting up another environment.

The personal-checklist migration `202609080003_personal_checklists.sql` was also
applied on 2026-09-08. Its save function is available to authenticated users and
denies anonymous execution.

Apply `migrations/202609080001_country_checklists.sql` to the application's Supabase
project after the existing migrations. It creates the template, tender checklist,
and attachment tables, access policies, application RPC, and a private
`checklist-documents` Storage bucket with a 20 MB per-file limit. Run the entire
file as one transaction using the Supabase SQL editor or your migration runner.
No templates are seeded: country administrators supply the actual requirements.

In a tender's **Compliance Checklist**, a country administrator can create and
edit templates for that country. A super administrator can manage either country.
Other users can select and apply their country's templates, then upload documents
against the requirements. Each user's checklist is saved per tender and country;
templates are shared across the country. Applying a template copies its requirements
so later template edits do not change existing tender checklists.

Completion is derived from saved uploaded documents. It cannot be manually toggled.
Removing an item's last attachment reopens it. Replacing a checklist requires
removing its attached documents first. File downloads use authenticated Storage
requests; temporary browser URLs are never stored as evidence. The package manifest
includes document IDs, names, and sizes.

Suggested acceptance checks after applying the migration:

- A Zimbabwe country admin can create/edit Zimbabwe templates but cannot write
  Zambia templates; an account manager cannot create/edit templates directly.
- Two users in the same country see the same templates and have separate tender
  checklist instances. Another country's templates are absent.
- Applying a template starts at zero completed items. A successful upload crosses
  off exactly its requirement and updates the completion total and manifest.
- Reopen the tender or reload the browser: checklist requirements and uploads
  remain available, and the document still downloads.
- Failed or oversized uploads leave the requirement incomplete. Removing one of
  two files keeps it completed; removing the last file reopens it.
- Updating a template does not modify an already applied checklist, and applying
  another template with attachments present is rejected by the database.
- Direct writes to tender checklist snapshots or attachment metadata without a
  matching uploaded Storage object are rejected.

The legacy .NET checklist controller is not used by this dashboard flow.

## Personal checklists

Users can choose **Create your own checklist** without an administrator template.
They can name it, add requirements, and save it for their tender. **Edit my checklist**
reopens an existing personal checklist; **Customize my checklist** starts from their
applied country template. These changes affect the user's tender checklist only.
Country template creation and editing remain restricted to administrators.

Personal checklists have a null `template_id` and use the same attachment storage
and automatic completion as template-based checklists. The save function validates
the current user and country, and preserves requirements that have uploaded files.
Users must remove those files before changing or deleting their requirement.
Adding further requirements preserves existing uploads and completion.

## Adding items and linking repository documents

`202609080004_checklist_document_links.sql` was applied to the configured project
on 2026-09-08. It adds atomic item creation, document linking, and the private
`repository-documents` Storage bucket.

Use **Add item** on an existing checklist to append a requirement. On each item,
choose a file to upload or use **Link from Documents** to select a saved repository
document for the tender's country (including regional documents). Both sources
complete the requirement. The same repository document can be used on different
items and tenders. Removing a link leaves the original repository document intact.

Documents & Specs now uploads and downloads the original file. Older records that
only contain document details show **Attach file**; an administrator must attach
the original file before those records can be linked. The picker has search and
refresh controls and disables duplicate links and records without a stored file.
Existing repository administration permissions remain in effect.

When an unused repository record is deleted, its checklist links are removed by a
foreign key cascade. Deletion is blocked when it would change a submitted or
approved checklist. Reopening a checklist reloads its current links and totals.

## Reference documents

Apply `migrations/202609080002_reference_documents.sql` to enable reference uploads.
The form requires only a document name and a file. Files are saved in the private
`reference-documents` bucket; metadata is saved per country, tender, and user.
The database rejects records without a matching uploaded file. Uploading the same
file again does not increase the count. Sample references have been removed.

The three-document total and submission use saved uploads. The database requires
three reference documents before submission. Deleting a reference from a draft or
revision reduces the count; reopening the tender reloads saved uploads.

After applying this migration, verify that a name without a file cannot be saved,
three different uploaded documents satisfy the requirement, an upload failure or
duplicate leaves the count unchanged, and saved documents download after reloading.

## Saved approvals and complete document download

`202609080005_submission_workflow.sql` and
`202609080006_review_revision_guard.sql` were applied on 2026-09-08.

The AM completes every mandatory checklist requirement, attaches at least one
checklist file and three reference documents, then chooses **Submit to HOD Review**.
The database constructs the submission from saved evidence; client-supplied file
lists, completed flags, approver identities, and approval stages are not trusted.
The enforced sequence is HOD → Technical → Committee → Approved. Each stage needs
an active reviewer with that role and country access. Administrators cannot skip
stages, and no user can approve their own submission.

Review & Approval lists the saved packages. Reviewers can download the original
files to assess them, approve the pending stage, or request a revision with comments.
A revision unlocks the AM's checklist. Resubmission restarts at HOD, increments the
revision number, and retains previous approval decisions. Stale revision numbers
and repeated approvals are rejected. The tender dialog reloads saved status on
open, focus, refresh, and every 20 seconds while visible.

Submitted and approved checklist requirements and attachments are locked by
database triggers. `review_package_files` preserves the exact source paths and
filenames that were submitted. Restrictive Storage policies prevent overwriting
or removing files referenced by a package. This includes linked repository files.
During a revision, the previous snapshot stays available until resubmission;
removing a file from the editable checklist does not delete the pinned bytes.

**Download all files (.zip)** is available after Committee approval in both the
tender dialog and Review & Approval. The authenticated endpoint checks country,
ownership/reviewer access, and approval status. It streams every checklist and
reference file plus `Approval_Record.json`, which includes the approval history
and archive path mapping. Repeated filenames receive unique numeric prefixes.
The download uses standard ZIP with a 4 GB package limit; individual documents
remain downloadable. A missing or unreadable source fails the download rather
than silently omitting it. Older sample review records have no file package and
must be replaced by a real submission from the tender checklist.

Database deployment metadata was inspected: migrations, RLS, privileges, evidence
triggers, Storage preservation policies, and the revision guard.

### Browser validation and decline decisions

On 2026-09-08, `202609080009_review_decline.sql` added an explicit **Decline
submission** action alongside **Accept** and **Request revision**. Declining
requires a reason and is restricted to the active reviewer's role. The AM sees
the declined stage and reason, can correct the documents, and can resubmit to HOD.
Earlier decisions stay in the history. A declined package cannot download as an
approved ZIP.

The authenticated Chrome end-to-end run passed all 17 recorded checks, including
AM checklist creation, real uploads and linking, mandatory evidence checks,
downloads by all three reviewer roles with byte-for-byte comparisons, country
and owner isolation, ordered approvals, declines by each role, visible reasons,
resubmission, stale revision rejection, and the final ZIP contents. The full
TypeScript check also passed. Disposable users and all fixture records and files
were removed; existing users and real submissions were not changed.

The repeatable test is `next-shadcn-admin-dashboard/scripts/test-review-workflow.mjs`.
Its setup is documented in `scripts/REVIEW_WORKFLOW_TEST.md` within that project.
The successful run report and screenshots are under
`next-shadcn-admin-dashboard/test-results/review-e2e-1788872187199/`.
The focused mobile check confirmed that the dialog fits within a 390 px viewport.
It also exposed a server/browser timestamp formatting mismatch on All Tenders;
that timestamp now renders after client data loading. The repeated browser check
reported no page errors, and the repeated TypeScript check passed. The mobile
evidence is `mobile-layout-verified.png` and `layout-check.json` in the run folder.

Reviewer provisioning remains necessary: at deployment, the project had one
active Zimbabwe HOD, no active Technical or Committee reviewers, and no Zambia
reviewers. Assign the intended people through user administration before expecting
packages to complete all three stages. No user roles were changed by this task.

Manual acceptance scenarios for a follow-up validation:

- Missing mandatory evidence or fewer than three references blocks submission,
  including direct RPC calls.
- AM submission survives reload and appears for the country's HOD; reviewers in
  another country and unrelated AMs cannot read it.
- HOD, Technical, and Committee approve in order. Wrong roles, self approval,
  duplicate actions, and approvals from an older revision fail.
- Edits and source-file deletion fail during review and after approval.
- A reviewer requests changes, the AM edits and resubmits, and the history remains.
- Before approval, the ZIP endpoint refuses access. After Committee approval,
  extract the ZIP and compare every file with its original, including linked
  documents, all references, and files with identical names.
