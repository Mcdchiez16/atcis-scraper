begin;

create table if not exists public.admin_audit_logs (
  id bigint generated always as identity primary key,
  actor_id uuid not null references auth.users(id) on delete restrict,
  target_id uuid references auth.users(id) on delete set null,
  action text not null check (action in ('user_created','user_updated','password_reset')),
  details jsonb not null default '{}'::jsonb check (jsonb_typeof(details) = 'object'),
  created_at timestamptz not null default now()
);

create index if not exists admin_audit_logs_created_at_idx on public.admin_audit_logs(created_at desc);
alter table public.admin_audit_logs enable row level security;
create policy admin_audit_read on public.admin_audit_logs for select to authenticated
  using ((public.atcis_profile()).role = 'super_admin');

revoke all on public.admin_audit_logs from anon, authenticated;
grant select on public.admin_audit_logs to authenticated;
grant all on public.admin_audit_logs to service_role;
grant usage, select on sequence public.admin_audit_logs_id_seq to service_role;

commit;
