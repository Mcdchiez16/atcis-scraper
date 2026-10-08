begin;

-- Task moves keep the same ID. Removing an existing ID is a deletion and is
-- reserved for country administrators and super administrators.
create or replace function public.atcis_pipeline_task_ids(p_payload jsonb)
returns table(task_id text)
language sql
immutable
set search_path = ''
as $$
  select distinct task->>'id'
  from jsonb_each(
    case
      when jsonb_typeof(p_payload->'board') = 'object' then p_payload->'board'
      else '{}'::jsonb
    end
  ) as board(column_id, tasks)
  cross join lateral jsonb_array_elements(
    case when jsonb_typeof(tasks) = 'array' then tasks else '[]'::jsonb end
  ) as task
  where jsonb_typeof(task) = 'object'
    and coalesce(task->>'id', '') <> '';
$$;

create or replace function public.enforce_app_record_deletion_roles()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
  actor public.profiles;
begin
  if auth.role() = 'service_role' then
    if tg_op = 'DELETE' then return old; end if;
    return new;
  end if;

  actor := public.atcis_profile();
  if actor.id is null then
    raise exception 'Authentication required' using errcode = '42501';
  end if;

  if tg_op = 'DELETE' and old.kind in ('pipeline', 'folder')
     and actor.role not in ('country_admin', 'super_admin') then
    raise exception 'Administrator required to delete % records', old.kind using errcode = '42501';
  end if;

  if tg_op = 'UPDATE' and old.kind = 'pipeline'
     and actor.role not in ('country_admin', 'super_admin')
     and exists (
       select 1
       from public.atcis_pipeline_task_ids(old.payload) as old_task
       where not exists (
         select 1
         from public.atcis_pipeline_task_ids(new.payload) as new_task
         where new_task.task_id = old_task.task_id
       )
     ) then
    raise exception 'Administrator required to delete pipeline tenders' using errcode = '42501';
  end if;

  if tg_op = 'DELETE' then return old; end if;
  return new;
end;
$$;

drop trigger if exists enforce_app_record_deletion_roles on public.app_records;
create trigger enforce_app_record_deletion_roles
before update or delete on public.app_records
for each row execute function public.enforce_app_record_deletion_roles();

create or replace function public.admin_clear_pipeline_records(p_owner_email text default null)
returns integer
language plpgsql
security definer
set search_path = ''
as $$
declare
  actor public.profiles := public.atcis_profile();
  target_owner text := nullif(lower(trim(p_owner_email)), '');
  deleted_count integer;
begin
  if actor.id is null then
    raise exception 'Authentication required' using errcode = '42501';
  end if;
  if actor.role not in ('country_admin', 'super_admin') then
    raise exception 'Administrator required to delete pipeline tenders' using errcode = '42501';
  end if;
  if target_owner in ('all', 'combined') then target_owner := null; end if;

  delete from public.app_records
  where kind = 'pipeline'
    and (target_owner is null or lower(id) = target_owner)
    and (actor.role = 'super_admin' or country = actor.country);

  get diagnostics deleted_count = row_count;
  return deleted_count;
end;
$$;

revoke all on function public.atcis_pipeline_task_ids(jsonb) from public, anon, authenticated;
revoke all on function public.enforce_app_record_deletion_roles() from public, anon, authenticated;
revoke all on function public.admin_clear_pipeline_records(text) from public, anon;
grant execute on function public.admin_clear_pipeline_records(text) to authenticated;

notify pgrst, 'reload schema';
commit;
