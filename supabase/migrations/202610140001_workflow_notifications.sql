begin;

create extension if not exists pg_net with schema extensions;

-- Workflow notifications are written only by trusted database triggers. Users
-- may read their own rows and mark them read through the narrow RPC below.
create table if not exists public.workflow_notifications (
  id uuid primary key default gen_random_uuid(),
  recipient_id uuid not null references public.profiles(id) on delete cascade,
  actor_id uuid references public.profiles(id) on delete set null,
  event_type text not null check (event_type in ('tender_assigned', 'documents_submitted')),
  title text not null check (char_length(title) between 1 and 240),
  message text not null check (char_length(message) between 1 and 2000),
  action_url text not null check (action_url like '/dashboard/%'),
  entity_type text not null check (entity_type in ('assignment', 'review')),
  entity_id text not null,
  country text not null check (country in ('ZW', 'ZM', 'ALL')),
  metadata jsonb not null default '{}'::jsonb check (jsonb_typeof(metadata) = 'object'),
  dedupe_key text not null,
  read_at timestamptz,
  created_at timestamptz not null default now(),
  unique (recipient_id, dedupe_key)
);

create index if not exists workflow_notifications_recipient_idx
  on public.workflow_notifications(recipient_id, created_at desc);
create index if not exists workflow_notifications_unread_idx
  on public.workflow_notifications(recipient_id, created_at desc)
  where read_at is null;

alter table public.workflow_notifications enable row level security;

drop policy if exists workflow_notifications_read_own on public.workflow_notifications;
create policy workflow_notifications_read_own
  on public.workflow_notifications for select to authenticated
  using (recipient_id = auth.uid());

revoke all on public.workflow_notifications from public, anon, authenticated;
grant select on public.workflow_notifications to authenticated;
grant all on public.workflow_notifications to service_role;

create or replace function public.mark_workflow_notifications_read(p_ids uuid[] default null)
returns integer
language plpgsql
security definer
set search_path = ''
as $$
declare
  changed integer;
begin
  if auth.uid() is null then
    raise exception 'Authentication required' using errcode = '42501';
  end if;

  update public.workflow_notifications
     set read_at = coalesce(read_at, now())
   where recipient_id = auth.uid()
     and read_at is null
     and (p_ids is null or id = any(p_ids));
  get diagnostics changed = row_count;
  return changed;
end;
$$;

revoke all on function public.mark_workflow_notifications_read(uuid[]) from public, anon;
grant execute on function public.mark_workflow_notifications_read(uuid[]) to authenticated;

-- This is a durable transactional outbox. It is not exposed through the Data
-- API to application users; only the service-role Edge Function may process it.
create table if not exists public.workflow_email_outbox (
  id uuid primary key default gen_random_uuid(),
  notification_id uuid not null unique references public.workflow_notifications(id) on delete cascade,
  recipient_email text not null check (char_length(recipient_email) between 3 and 254),
  recipient_name text not null default '',
  subject text not null check (char_length(subject) between 1 and 300),
  message text not null check (char_length(message) between 1 and 2000),
  action_url text not null check (action_url like '/dashboard/%'),
  event_type text not null,
  status text not null default 'pending' check (status in ('pending', 'processing', 'sent', 'failed')),
  attempts integer not null default 0 check (attempts between 0 and 20),
  provider_message_id text,
  last_error text,
  created_at timestamptz not null default now(),
  processing_at timestamptz,
  sent_at timestamptz
);

create index if not exists workflow_email_outbox_status_idx
  on public.workflow_email_outbox(status, created_at);

alter table public.workflow_email_outbox enable row level security;
revoke all on public.workflow_email_outbox from public, anon, authenticated;
grant all on public.workflow_email_outbox to service_role;

create or replace function public.queue_workflow_notification_email()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
  recipient public.profiles;
begin
  select * into recipient from public.profiles where id = new.recipient_id and active;
  if recipient.id is null then
    return new;
  end if;

  insert into public.workflow_email_outbox(
    notification_id, recipient_email, recipient_name, subject, message,
    action_url, event_type
  ) values (
    new.id, lower(recipient.email), recipient.name, new.title, new.message,
    new.action_url, new.event_type
  ) on conflict (notification_id) do nothing;

  return new;
end;
$$;

revoke all on function public.queue_workflow_notification_email() from public, anon, authenticated;

drop trigger if exists workflow_notification_email_outbox on public.workflow_notifications;
create trigger workflow_notification_email_outbox
after insert on public.workflow_notifications
for each row execute function public.queue_workflow_notification_email();

-- Preserve the authenticated manager who made an assignment. The existing RPC
-- already verifies their role and country before this trigger executes.
create or replace function public.stamp_assignment_actor()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
  actor public.profiles := public.atcis_profile();
begin
  if new.kind = 'assignment'
     and actor.id is not null
     and actor.role in ('hod', 'country_admin', 'super_admin')
     and (
       tg_op = 'INSERT'
       or old.owner_email is distinct from new.owner_email
       or old.payload->>'assignedToEmail' is distinct from new.payload->>'assignedToEmail'
     ) then
    new.payload := new.payload || jsonb_build_object(
      'assignedByHodName', actor.name,
      'assignedByEmail', lower(actor.email),
      'assignedByUserId', actor.id
    );
  end if;
  return new;
end;
$$;

revoke all on function public.stamp_assignment_actor() from public, anon, authenticated;

drop trigger if exists stamp_assignment_actor_before_save on public.app_records;
create trigger stamp_assignment_actor_before_save
before insert or update of payload, owner_email on public.app_records
for each row execute function public.stamp_assignment_actor();

create or replace function public.create_assignment_notification()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
  recipient public.profiles;
  actor_uuid uuid;
  tender_title text;
  tender_ref text;
  due_date text;
begin
  if new.kind <> 'assignment' then
    return new;
  end if;
  if tg_op = 'UPDATE'
     and old.owner_email is not distinct from new.owner_email
     and old.payload->>'assignedToEmail' is not distinct from new.payload->>'assignedToEmail' then
    return new;
  end if;

  select * into recipient
    from public.profiles
   where active and role = 'account_manager' and lower(email) = lower(new.owner_email)
   limit 1;
  if recipient.id is null then
    return new;
  end if;

  select p.id into actor_uuid
    from public.profiles p
   where p.id::text = new.payload->>'assignedByUserId'
   limit 1;

  tender_title := coalesce(nullif(new.payload->>'tenderTitle', ''), 'Tender assignment');
  tender_ref := coalesce(nullif(new.payload->>'tenderRef', ''), new.id);
  due_date := nullif(new.payload->>'dueDate', '');

  insert into public.workflow_notifications(
    recipient_id, actor_id, event_type, title, message, action_url,
    entity_type, entity_id, country, metadata, dedupe_key
  ) values (
    recipient.id,
    actor_uuid,
    'tender_assigned',
    'New tender assigned: ' || left(tender_ref, 120),
    left(
      coalesce(new.payload->>'assignedByHodName', 'Your HOD') ||
      ' assigned you "' || tender_title || '"' ||
      case when due_date is null then '.' else '. Due ' || due_date || '.' end,
      2000
    ),
    '/dashboard/team',
    'assignment',
    new.id,
    new.country,
    jsonb_build_object(
      'tenderRef', tender_ref,
      'tenderTitle', tender_title,
      'dueDate', due_date,
      'priority', new.payload->>'priority'
    ),
    'assignment:' || new.id || ':' || recipient.id::text
  ) on conflict (recipient_id, dedupe_key) do nothing;

  return new;
end;
$$;

revoke all on function public.create_assignment_notification() from public, anon, authenticated;

drop trigger if exists assignment_workflow_notification on public.app_records;
create trigger assignment_workflow_notification
after insert or update of payload, owner_email on public.app_records
for each row execute function public.create_assignment_notification();

create or replace function public.create_review_submission_notification()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
  submitter_id uuid;
  responsible_hod_id uuid;
  recipient public.profiles;
  tender_title text;
  tender_ref text;
  revision_no text;
begin
  if new.kind <> 'review' or new.payload->>'overallStatus' <> 'In Review' then
    return new;
  end if;
  if tg_op = 'UPDATE'
     and old.payload->>'revision' is not distinct from new.payload->>'revision' then
    return new;
  end if;

  select p.id into submitter_id
    from public.profiles p
   where lower(p.email) = lower(new.owner_email)
   limit 1;

  tender_title := coalesce(nullif(new.payload->>'tenderTitle', ''), 'Tender document package');
  tender_ref := coalesce(nullif(new.payload->>'tenderRef', ''), new.payload->>'tenderKey', new.id);
  revision_no := coalesce(nullif(new.payload->>'revision', ''), '1');

  -- Prefer the HOD who assigned this tender to the submitting AM. Older
  -- assignments without actor metadata safely fall back to the country's HODs.
  select p.id into responsible_hod_id
    from public.app_records a
    join public.profiles p on p.id::text = a.payload->>'assignedByUserId'
   where a.kind = 'assignment'
     and a.country = new.country
     and lower(a.owner_email) = lower(new.owner_email)
     and p.active
     and p.role = 'hod'
     and (
       lower(coalesce(a.payload->>'tenderRef', '')) = lower(tender_ref)
       or lower(coalesce(a.payload->>'tenderRef', '')) = lower(coalesce(new.payload->>'tenderKey', ''))
     )
   order by a.updated_at desc
   limit 1;

  for recipient in
    select p.*
      from public.profiles p
     where p.active
       and p.role = 'hod'
       and (
         (responsible_hod_id is not null and p.id = responsible_hod_id)
         or (
           responsible_hod_id is null
           and (p.country = new.country or p.country = 'ALL')
         )
       )
  loop
    insert into public.workflow_notifications(
      recipient_id, actor_id, event_type, title, message, action_url,
      entity_type, entity_id, country, metadata, dedupe_key
    ) values (
      recipient.id,
      submitter_id,
      'documents_submitted',
      'Tender documents awaiting approval',
      left(
        coalesce(new.payload->>'submittedByName', new.owner_email) ||
        ' submitted documents for "' || tender_title || '" (' || tender_ref || ').',
        2000
      ),
      '/dashboard/reviews',
      'review',
      new.id,
      new.country,
      jsonb_build_object(
        'tenderRef', tender_ref,
        'tenderTitle', tender_title,
        'revision', revision_no,
        'submittedByName', new.payload->>'submittedByName'
      ),
      'review-submitted:' || new.id || ':revision:' || revision_no
    ) on conflict (recipient_id, dedupe_key) do nothing;
  end loop;

  return new;
end;
$$;

revoke all on function public.create_review_submission_notification() from public, anon, authenticated;

drop trigger if exists review_submission_workflow_notification on public.app_records;
create trigger review_submission_workflow_notification
after insert or update of payload on public.app_records
for each row execute function public.create_review_submission_notification();

-- Deliver queued workflow mail through the same authenticated Edge Function as
-- tender alerts. pg_net makes this asynchronous, so user actions stay fast.
create or replace function public.notify_workflow_email()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
  function_key text;
begin
  if new.status <> 'pending'
     or (tg_op = 'UPDATE' and old.status is not distinct from new.status) then
    return new;
  end if;

  select decrypted_secret
    into function_key
    from vault.decrypted_secrets
   where name = 'atcis_tender_webhook_secret'
   limit 1;

  if function_key is null then
    raise warning 'Workflow email webhook key is not configured';
    return new;
  end if;

  perform net.http_post(
    url := 'https://pqqymbdbkwltzydymild.supabase.co/functions/v1/send-tender-alert',
    headers := jsonb_build_object(
      'Content-Type', 'application/json',
      'x-atcis-webhook-secret', function_key
    ),
    body := jsonb_build_object(
      'type', tg_op,
      'table', tg_table_name,
      'schema', tg_table_schema,
      'record', to_jsonb(new),
      'old_record', case when tg_op = 'UPDATE' then to_jsonb(old) else null end
    ),
    timeout_milliseconds := 5000
  );

  return new;
end;
$$;

revoke all on function public.notify_workflow_email() from public, anon, authenticated;

drop trigger if exists workflow_email_webhook on public.workflow_email_outbox;
create trigger workflow_email_webhook
after insert or update of status on public.workflow_email_outbox
for each row
execute function public.notify_workflow_email();

notify pgrst, 'reload schema';
commit;
