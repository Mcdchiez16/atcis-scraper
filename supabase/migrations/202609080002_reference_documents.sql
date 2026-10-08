begin;

create table public.tender_reference_documents (
  id uuid primary key default gen_random_uuid(),
  country text not null check (country in ('ZW','ZM')),
  tender_key text not null check (length(btrim(tender_key)) between 1 and 500),
  owner_id uuid not null default auth.uid() references public.profiles(id),
  name text not null check (length(btrim(name)) between 1 and 200),
  file_name text not null check (length(file_name) between 1 and 255),
  file_size bigint not null check (file_size between 1 and 20971520),
  content_hash text not null check (content_hash ~ '^[a-f0-9]{64}$'),
  storage_path text not null unique,
  created_at timestamptz not null default now(),
  unique(country,tender_key,owner_id,content_hash)
);

alter table public.tender_reference_documents enable row level security;
revoke all on public.tender_reference_documents from public,anon,authenticated;
grant select,insert,delete on public.tender_reference_documents to authenticated;

create policy reference_documents_read on public.tender_reference_documents for select to authenticated
  using (public.atcis_country_allowed(country) and (owner_id=auth.uid()
    or (public.atcis_profile()).role in ('hod','technical_review','committee','country_admin','super_admin')));
create policy reference_documents_create on public.tender_reference_documents for insert to authenticated
  with check (owner_id=auth.uid() and public.atcis_country_allowed(country));
create policy reference_documents_remove on public.tender_reference_documents for delete to authenticated
  using (owner_id=auth.uid() and public.atcis_country_allowed(country));

insert into storage.buckets(id,name,public,file_size_limit)
values('reference-documents','reference-documents',false,20971520);

-- The file is uploaded before a reference can be saved or counted.
create policy reference_files_upload on storage.objects for insert to authenticated
  with check (bucket_id='reference-documents'
    and split_part(name,'/',1) in ('ZW','ZM')
    and public.atcis_country_allowed(split_part(name,'/',1))
    and split_part(name,'/',2)=auth.uid()::text);
create policy reference_files_read on storage.objects for select to authenticated
  using (bucket_id='reference-documents' and public.atcis_country_allowed(split_part(name,'/',1))
    and (split_part(name,'/',2)=auth.uid()::text or exists(
      select 1 from public.tender_reference_documents d where d.storage_path=storage.objects.name
    )));
create policy reference_files_remove on storage.objects for delete to authenticated
  using (bucket_id='reference-documents' and public.atcis_country_allowed(split_part(name,'/',1))
    and split_part(name,'/',2)=auth.uid()::text
    and not exists(select 1 from public.tender_reference_documents d where d.storage_path=storage.objects.name));

create function public.validate_reference_document() returns trigger
language plpgsql security definer set search_path = '' as $$
begin
  if new.owner_id is distinct from auth.uid() or not public.atcis_country_allowed(new.country) then
    raise exception 'Reference document access denied' using errcode='42501';
  end if;
  if split_part(new.storage_path,'/',1)<>new.country
    or split_part(new.storage_path,'/',2)<>new.owner_id::text
    or not exists(select 1 from storage.objects o where o.bucket_id='reference-documents'
      and o.name=new.storage_path and (o.metadata->>'size')::bigint=new.file_size) then
    raise exception 'A reference document upload is required';
  end if;
  return new;
end;
$$;
create trigger validate_reference_document before insert on public.tender_reference_documents
  for each row execute function public.validate_reference_document();

commit;
