begin;

create function public.valid_checklist_items(items jsonb) returns boolean
language plpgsql immutable set search_path = '' as $$
begin
  if jsonb_typeof(items) <> 'array' then return false; end if;
  if jsonb_array_length(items) not between 1 and 100 then return false; end if;
  return not exists (
    select 1 from jsonb_array_elements(items) i
    where jsonb_typeof(i) <> 'object'
      or jsonb_typeof(i->'id') is distinct from 'string'
      or jsonb_typeof(i->'item') is distinct from 'string'
      or coalesce(length(btrim(i->>'id')),0) not between 1 and 100
      or coalesce(length(btrim(i->>'item')),0) not between 1 and 500
      or coalesce(i->>'status','') not in ('Mandatory','Conditional','Standard','Optional')
      or jsonb_typeof(i->'description') is distinct from 'string'
      or length(i->>'description') > 2000
      or jsonb_typeof(i->'category') is distinct from 'string'
      or length(i->>'category') > 100
      or i ? 'attachedFiles'
  ) and (select count(distinct i->>'id') from jsonb_array_elements(items) i) = jsonb_array_length(items);
end;
$$;

create table public.checklist_templates (
  id uuid primary key default gen_random_uuid(),
  country text not null check (country in ('ZW','ZM')),
  name text not null check (length(btrim(name)) between 1 and 200),
  items jsonb not null check (public.valid_checklist_items(items)),
  created_by uuid not null default auth.uid() references public.profiles(id),
  created_at timestamptz not null default now(),
  unique(country,name)
);

create table public.tender_checklists (
  id uuid primary key default gen_random_uuid(),
  country text not null check (country in ('ZW','ZM')),
  tender_key text not null check (length(btrim(tender_key)) between 1 and 500),
  owner_id uuid not null references public.profiles(id),
  template_id uuid not null references public.checklist_templates(id),
  template_name text not null,
  items jsonb not null check (public.valid_checklist_items(items)),
  created_at timestamptz not null default now(),
  unique(country,tender_key,owner_id)
);

create table public.checklist_attachments (
  id uuid primary key default gen_random_uuid(),
  checklist_id uuid not null references public.tender_checklists(id) on delete cascade,
  item_id text not null,
  name text not null check (length(name) between 1 and 255),
  size bigint not null check (size between 1 and 20971520),
  storage_path text not null unique,
  created_at timestamptz not null default now()
);
create index checklist_attachments_checklist_idx on public.checklist_attachments(checklist_id);

alter table public.checklist_templates enable row level security;
alter table public.tender_checklists enable row level security;
alter table public.checklist_attachments enable row level security;
revoke all on public.checklist_templates, public.tender_checklists, public.checklist_attachments from public,authenticated;
grant select, insert on public.checklist_templates to authenticated;
grant update(name,items) on public.checklist_templates to authenticated;
grant select on public.tender_checklists to authenticated;
grant select,insert,delete on public.checklist_attachments to authenticated;
revoke all on public.checklist_templates, public.tender_checklists, public.checklist_attachments from anon;

create policy checklist_templates_read on public.checklist_templates for select to authenticated
  using (public.atcis_country_allowed(country));
create policy checklist_templates_create on public.checklist_templates for insert to authenticated
  with check (created_by=auth.uid() and (
    (public.atcis_profile()).role='super_admin'
    or ((public.atcis_profile()).role='country_admin' and (public.atcis_profile()).country=country)
  ));
create policy checklist_templates_edit on public.checklist_templates for update to authenticated
  using ((public.atcis_profile()).role='super_admin'
    or ((public.atcis_profile()).role='country_admin' and (public.atcis_profile()).country=country))
  with check ((public.atcis_profile()).role='super_admin'
    or ((public.atcis_profile()).role='country_admin' and (public.atcis_profile()).country=country));
create policy tender_checklists_read on public.tender_checklists for select to authenticated
  using (public.atcis_country_allowed(country) and (owner_id=auth.uid()
    or (public.atcis_profile()).role in ('hod','technical_review','committee','country_admin','super_admin')));
create policy checklist_attachments_read on public.checklist_attachments for select to authenticated
  using (exists(select 1 from public.tender_checklists c where c.id=checklist_id));
create policy checklist_attachments_create on public.checklist_attachments for insert to authenticated
  with check (exists(select 1 from public.tender_checklists c where c.id=checklist_id and c.owner_id=auth.uid()));
create policy checklist_attachments_delete on public.checklist_attachments for delete to authenticated
  using (exists(select 1 from public.tender_checklists c where c.id=checklist_id and c.owner_id=auth.uid()));

-- Only this RPC can copy administrator requirements onto a tender. Clients
-- cannot write their own requirements or a manually completed flag.
create function public.apply_checklist_template(p_template_id uuid, p_tender_key text)
returns public.tender_checklists language plpgsql security definer set search_path = '' as $$
declare
  actor public.profiles := public.atcis_profile();
  template public.checklist_templates;
  result public.tender_checklists;
begin
  if actor.id is null then raise exception 'Authentication required' using errcode='42501'; end if;
  select * into template from public.checklist_templates where id=p_template_id;
  if template.id is null or not public.atcis_country_allowed(template.country) then
    raise exception 'Template unavailable for your country' using errcode='42501';
  end if;
  if p_tender_key is null or length(btrim(p_tender_key)) not between 1 and 500 then
    raise exception 'Tender is required';
  end if;
  perform pg_advisory_xact_lock(hashtextextended(actor.id::text||template.country||p_tender_key,0));
  select * into result from public.tender_checklists
    where country=template.country and tender_key=p_tender_key and owner_id=actor.id for update;
  if result.id is not null and exists(select 1 from public.checklist_attachments where checklist_id=result.id) then
    raise exception 'Remove attached documents before replacing this checklist';
  end if;
  insert into public.tender_checklists(country,tender_key,owner_id,template_id,template_name,items)
    values(template.country,p_tender_key,actor.id,template.id,template.name,template.items)
    on conflict(country,tender_key,owner_id) do update
    set template_id=excluded.template_id,template_name=excluded.template_name,items=excluded.items
    returning * into result;
  return result;
end;
$$;
revoke all on function public.apply_checklist_template(uuid,text) from public,anon;
grant execute on function public.apply_checklist_template(uuid,text) to authenticated;

insert into storage.buckets(id,name,public,file_size_limit)
values('checklist-documents','checklist-documents',false,20971520);

-- Paths: country / owner / checklist / unique-file. No overwrite policy:
-- uploaded evidence stays immutable until explicitly removed.
create policy checklist_files_upload on storage.objects for insert to authenticated
  with check (bucket_id='checklist-documents' and exists(
    select 1 from public.tender_checklists c where c.id::text=split_part(name,'/',3)
      and c.country=split_part(name,'/',1) and c.owner_id::text=split_part(name,'/',2)
      and c.owner_id=auth.uid()
  ));
create policy checklist_files_read on storage.objects for select to authenticated
  using (bucket_id='checklist-documents' and exists(
    select 1 from public.tender_checklists c where c.id::text=split_part(name,'/',3)
      and c.country=split_part(name,'/',1) and c.owner_id::text=split_part(name,'/',2)
  ));
create policy checklist_files_remove on storage.objects for delete to authenticated
  using (bucket_id='checklist-documents' and exists(
    select 1 from public.tender_checklists c where c.id::text=split_part(name,'/',3)
      and c.country=split_part(name,'/',1) and c.owner_id::text=split_part(name,'/',2)
      and c.owner_id=auth.uid()
  ) and not exists(select 1 from public.checklist_attachments a where a.storage_path=storage.objects.name));

create function public.validate_checklist_attachment() returns trigger
language plpgsql security definer set search_path = '' as $$
declare c public.tender_checklists;
begin
  select * into c from public.tender_checklists where id=new.checklist_id for update;
  if c.owner_id is distinct from auth.uid() or not public.atcis_country_allowed(c.country) then
    raise exception 'Checklist access denied' using errcode='42501';
  end if;
  if not exists(select 1 from jsonb_array_elements(c.items) i where i->>'id'=new.item_id) then
    raise exception 'Checklist requirement not found';
  end if;
  if split_part(new.storage_path,'/',1)<>c.country
    or split_part(new.storage_path,'/',2)<>c.owner_id::text
    or split_part(new.storage_path,'/',3)<>c.id::text
    or not exists(select 1 from storage.objects o where o.bucket_id='checklist-documents'
      and o.name=new.storage_path and (o.metadata->>'size')::bigint=new.size) then
    raise exception 'Upload the document before completing this requirement';
  end if;
  return new;
end;
$$;
create trigger validate_checklist_attachment before insert on public.checklist_attachments
  for each row execute function public.validate_checklist_attachment();

commit;
