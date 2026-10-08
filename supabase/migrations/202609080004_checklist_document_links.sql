begin;

insert into storage.buckets(id,name,public,file_size_limit)
values('repository-documents','repository-documents',false,20971520);

alter table public.checklist_attachments
  add column storage_bucket text not null default 'checklist-documents'
    check (storage_bucket in ('checklist-documents','repository-documents')),
  add column repository_document_id text,
  add column original_file_name text,
  add column repository_document_kind text not null default 'document' check (repository_document_kind='document'),
  add constraint checklist_repository_document_fk foreign key(repository_document_kind,repository_document_id)
    references public.app_records(kind,id) on delete cascade,
  add constraint checklist_attachment_source_check check (
    (repository_document_id is null and storage_bucket='checklist-documents')
    or (repository_document_id is not null and storage_bucket='repository-documents')
  );
alter table public.checklist_attachments drop constraint checklist_attachments_storage_path_key;
create unique index checklist_uploaded_file_unique on public.checklist_attachments(storage_path)
  where repository_document_id is null;
create unique index checklist_link_unique on public.checklist_attachments(checklist_id,item_id,repository_document_id)
  where repository_document_id is not null;

create policy repository_files_upload on storage.objects for insert to authenticated
  with check (bucket_id='repository-documents' and split_part(name,'/',2)=auth.uid()::text
    and ((public.atcis_profile()).role='super_admin'
      or ((public.atcis_profile()).role='country_admin'
        and split_part(name,'/',1)=(public.atcis_profile()).country))
    and split_part(name,'/',1) in ('ZW','ZM','ALL'));
create policy repository_files_read on storage.objects for select to authenticated
  using (bucket_id='repository-documents' and public.atcis_country_allowed(split_part(name,'/',1)));
create policy repository_files_remove on storage.objects for delete to authenticated
  using (bucket_id='repository-documents'
    and ((public.atcis_profile()).role='super_admin'
      or ((public.atcis_profile()).role='country_admin'
        and split_part(name,'/',1)=(public.atcis_profile()).country))
    and not exists(select 1 from public.app_records d where d.kind='document'
      and d.payload->>'storagePath'=storage.objects.name)
    and not exists(select 1 from public.checklist_attachments a
      where a.storage_bucket='repository-documents' and a.storage_path=storage.objects.name));

create function public.validate_repository_file() returns trigger
language plpgsql security definer set search_path = '' as $$
begin
  if new.kind<>'document' or coalesce(new.payload->>'storagePath','')='' then return new; end if;
  if coalesce(new.payload->>'storageBucket','')<>'repository-documents'
    or split_part(new.payload->>'storagePath','/',1)<>new.country
    or coalesce(new.payload->>'fileName','')=''
    or not exists(select 1 from storage.objects o where o.bucket_id='repository-documents'
      and o.name=new.payload->>'storagePath'
      and (o.metadata->>'size')::bigint=(new.payload->>'sizeBytes')::bigint) then
    raise exception 'Upload the repository document before saving it';
  end if;
  return new;
end;
$$;
create trigger validate_repository_file before insert or update on public.app_records
  for each row execute function public.validate_repository_file();

create or replace function public.validate_checklist_attachment() returns trigger
language plpgsql security definer set search_path = '' as $$
declare
  c public.tender_checklists;
  d public.app_records;
begin
  select * into c from public.tender_checklists where id=new.checklist_id for update;
  if c.owner_id is distinct from auth.uid() or not public.atcis_country_allowed(c.country) then
    raise exception 'Checklist access denied' using errcode='42501';
  end if;
  if not exists(select 1 from jsonb_array_elements(c.items) i where i->>'id'=new.item_id) then
    raise exception 'Checklist requirement not found';
  end if;
  if new.repository_document_id is not null then
    select * into d from public.app_records where kind='document' and id=new.repository_document_id;
    if d.id is null or d.country not in (c.country,'ALL') then
      raise exception 'Document unavailable for this country' using errcode='42501';
    end if;
    if coalesce(d.payload->>'storagePath','')='' then
      raise exception 'This document has no file yet. Attach a file on the Documents page first';
    end if;
    new.storage_bucket := 'repository-documents';
    new.storage_path := d.payload->>'storagePath';
    new.name := left(d.payload->>'name',255);
    new.original_file_name := d.payload->>'fileName';
    new.size := (d.payload->>'sizeBytes')::bigint;
  else
    if new.storage_bucket<>'checklist-documents'
      or split_part(new.storage_path,'/',1)<>c.country
      or split_part(new.storage_path,'/',2)<>c.owner_id::text
      or split_part(new.storage_path,'/',3)<>c.id::text then
      raise exception 'Checklist file access denied' using errcode='42501';
    end if;
  end if;
  if not exists(select 1 from storage.objects o where o.bucket_id=new.storage_bucket
    and o.name=new.storage_path and (o.metadata->>'size')::bigint=new.size) then
    raise exception 'Upload a document before completing this requirement';
  end if;
  return new;
end;
$$;

create function public.link_checklist_document(p_checklist_id uuid,p_item_id text,p_document_id text)
returns public.checklist_attachments language plpgsql security definer set search_path = '' as $$
declare result public.checklist_attachments;
begin
  -- The attachment trigger validates the owner, country, item, and stored file,
  -- and derives the attachment metadata from the actual repository record.
  insert into public.checklist_attachments(checklist_id,item_id,repository_document_id)
    values(p_checklist_id,p_item_id,p_document_id) returning * into result;
  return result;
end;
$$;
revoke all on function public.link_checklist_document(uuid,text,text) from public,anon;
grant execute on function public.link_checklist_document(uuid,text,text) to authenticated;

create function public.add_checklist_item(p_checklist_id uuid,p_item jsonb)
returns public.tender_checklists language plpgsql security definer set search_path = '' as $$
declare c public.tender_checklists;
begin
  select * into c from public.tender_checklists where id=p_checklist_id for update;
  if c.id is null or c.owner_id is distinct from auth.uid() or not public.atcis_country_allowed(c.country) then
    raise exception 'Checklist access denied' using errcode='42501';
  end if;
  if not coalesce(public.valid_checklist_items(c.items||jsonb_build_array(p_item)),false) then
    raise exception 'Enter a valid requirement. A checklist can contain up to 100 items';
  end if;
  update public.tender_checklists set items=c.items||jsonb_build_array(p_item),template_id=null
    where id=c.id returning * into c;
  return c;
end;
$$;
revoke all on function public.add_checklist_item(uuid,jsonb) from public,anon;
grant execute on function public.add_checklist_item(uuid,jsonb) to authenticated;

notify pgrst, 'reload schema';
commit;
