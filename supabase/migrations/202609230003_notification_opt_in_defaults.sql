begin;

alter table public.notification_preferences
  alter column enabled set default false,
  alter column email_enabled set default false;

update public.notification_preferences
set
  enabled = false,
  email_enabled = false,
  preferences = jsonb_set(
    jsonb_set(preferences, '{enabled}', 'false'::jsonb, true),
    '{channels}',
    coalesce(preferences -> 'channels', '{}'::jsonb) || '{"email": false}'::jsonb,
    true
  ),
  updated_at = now();

commit;
