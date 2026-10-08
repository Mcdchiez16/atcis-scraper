begin;

create or replace function public.enforce_supplier_deletion_roles()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
  actor public.profiles;
  target_kind text;
begin
  if auth.role() = 'service_role' then
    if tg_op = 'DELETE' then return old; end if;
    return new;
  end if;

  target_kind := case when tg_op = 'DELETE' then old.kind else new.kind end;
  if target_kind <> 'supplier' then
    if tg_op = 'DELETE' then return old; end if;
    return new;
  end if;

  actor := public.atcis_profile();
  if actor.id is null then
    raise exception 'Authentication required' using errcode = '42501';
  end if;

  if actor.role not in ('country_admin', 'super_admin') then
    if tg_op = 'DELETE' then
      raise exception 'Administrator required to delete supplier records' using errcode = '42501';
    end if;
    if coalesce((new.payload->>'deleted')::boolean, false)
       or lower(coalesce(new.payload->>'status', '')) = 'inactive' then
      raise exception 'Administrator required to delete supplier records' using errcode = '42501';
    end if;
  end if;

  if tg_op = 'DELETE' then return old; end if;
  return new;
end;
$$;

drop trigger if exists enforce_supplier_deletion_roles on public.app_records;
create trigger enforce_supplier_deletion_roles
before insert or update or delete on public.app_records
for each row execute function public.enforce_supplier_deletion_roles();

revoke all on function public.enforce_supplier_deletion_roles() from public, anon, authenticated;
revoke all on function public.save_app_record(text, text, jsonb, boolean) from public, anon;
grant execute on function public.save_app_record(text, text, jsonb, boolean) to authenticated;

commit;
