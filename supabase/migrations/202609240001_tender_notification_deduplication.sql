begin;

create table if not exists public.tender_notification_deliveries (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null references public.profiles(id) on delete cascade,
  tender_id text not null,
  dedupe_key text not null,
  alert_type text not null default 'new-tender',
  recipient_email text not null,
  status text not null default 'pending' check (status in ('pending', 'sent')),
  provider_message_id text,
  created_at timestamptz not null default now(),
  sent_at timestamptz,
  unique (user_id, alert_type, tender_id),
  unique (user_id, alert_type, dedupe_key)
);

create index if not exists tender_notification_deliveries_created_idx
  on public.tender_notification_deliveries(created_at desc);

alter table public.tender_notification_deliveries enable row level security;

-- Delivery state is internal infrastructure. Only the Edge Function's
-- service-role client can read or mutate it.
revoke all on public.tender_notification_deliveries from public, anon, authenticated;
grant all on public.tender_notification_deliveries to service_role;

commit;
