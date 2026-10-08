begin;

create table if not exists public.notification_preferences (
  user_id uuid primary key references public.profiles(id) on delete cascade,
  email text not null check (char_length(email) between 3 and 254),
  enabled boolean not null default false,
  email_enabled boolean not null default true,
  country_scope text not null default 'ALL' check (country_scope in ('ZW', 'ZM', 'ALL')),
  alert_types text[] not null default array['new-tender']::text[],
  preferences jsonb not null default '{}'::jsonb check (jsonb_typeof(preferences) = 'object'),
  updated_at timestamptz not null default now()
);

create index if not exists notification_preferences_email_alert_idx
  on public.notification_preferences(country_scope)
  where enabled and email_enabled;

alter table public.notification_preferences enable row level security;

drop policy if exists notification_preferences_read_own on public.notification_preferences;
create policy notification_preferences_read_own
  on public.notification_preferences for select to authenticated
  using (user_id = auth.uid());

drop policy if exists notification_preferences_insert_own on public.notification_preferences;
create policy notification_preferences_insert_own
  on public.notification_preferences for insert to authenticated
  with check (user_id = auth.uid());

drop policy if exists notification_preferences_update_own on public.notification_preferences;
create policy notification_preferences_update_own
  on public.notification_preferences for update to authenticated
  using (user_id = auth.uid())
  with check (user_id = auth.uid());

revoke all on public.notification_preferences from anon, authenticated;
grant select, insert, update on public.notification_preferences to authenticated;
grant all on public.notification_preferences to service_role;

commit;
