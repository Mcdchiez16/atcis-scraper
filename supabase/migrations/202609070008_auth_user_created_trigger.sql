-- Trigger to ensure any user created via Supabase Dashboard or Admin panel
-- gets a profile and is flagged to change password on first login
create or replace function public.handle_new_user()
returns trigger
language plpgsql
security definer set search_path = ''
as $$
begin
  insert into public.profiles (id, email, name, role, country, department, active, must_change_password)
  values (
    new.id,
    new.email,
    coalesce(new.raw_user_meta_data->>'name', split_part(new.email, '@', 1)),
    coalesce(new.raw_user_meta_data->>'role', 'account_manager'),
    coalesce(new.raw_user_meta_data->>'country', 'ZW'),
    new.raw_user_meta_data->>'department',
    true,
    true
  )
  on conflict (id) do nothing;
  return new;
end;
$$;

drop trigger if exists on_auth_user_created on auth.users;
create trigger on_auth_user_created
  after insert on auth.users
  for each row execute function public.handle_new_user();
