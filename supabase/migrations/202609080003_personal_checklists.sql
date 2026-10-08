begin;

-- A null template_id identifies a checklist created or customized by its owner.
alter table public.tender_checklists alter column template_id drop not null;

create function public.save_personal_checklist(
  p_country text, p_tender_key text, p_name text, p_items jsonb, p_checklist_id uuid default null
) returns public.tender_checklists
language plpgsql security definer set search_path = '' as $$
declare
  actor public.profiles := public.atcis_profile();
  existing public.tender_checklists;
  result public.tender_checklists;
begin
  if actor.id is null or not coalesce(public.atcis_country_allowed(p_country),false) then
    raise exception 'Checklist access denied' using errcode='42501';
  end if;
  if coalesce(p_country,'') not in ('ZW','ZM')
    or coalesce(length(btrim(p_tender_key)),0) not between 1 and 500
    or coalesce(length(btrim(p_name)),0) not between 1 and 200
    or not coalesce(public.valid_checklist_items(p_items),false) then
    raise exception 'Enter a checklist name and between 1 and 100 valid requirements';
  end if;

  -- Use the same lock as applying a country template and lock the checklist
  -- row so concurrent uploads cannot attach evidence to a removed requirement.
  perform pg_advisory_xact_lock(hashtextextended(actor.id::text||p_country||p_tender_key,0));
  select * into existing from public.tender_checklists
    where country=p_country and tender_key=p_tender_key and owner_id=actor.id for update;
  if existing.id is distinct from p_checklist_id then
    raise exception 'This checklist has changed. Reload it before saving';
  end if;

  -- Keep requirements with uploaded evidence intact, including their IDs.
  -- New requirements and edits to requirements without files are allowed.
  if existing.id is not null and exists(
    select 1 from public.checklist_attachments a
    join lateral jsonb_array_elements(existing.items) old_item on old_item->>'id'=a.item_id
    where a.checklist_id=existing.id and not exists(
      select 1 from jsonb_array_elements(p_items) new_item where new_item=old_item
    )
  ) then
    raise exception 'Remove attached documents before changing or deleting their requirement';
  end if;

  if existing.id is null then
    insert into public.tender_checklists(country,tender_key,owner_id,template_id,template_name,items)
      values(p_country,p_tender_key,actor.id,null,btrim(p_name),p_items) returning * into result;
  else
    update public.tender_checklists set template_id=null,template_name=btrim(p_name),items=p_items
      where id=existing.id returning * into result;
  end if;
  return result;
end;
$$;

revoke all on function public.save_personal_checklist(text,text,text,jsonb,uuid) from public,anon;
grant execute on function public.save_personal_checklist(text,text,text,jsonb,uuid) to authenticated;

notify pgrst, 'reload schema';
commit;
