begin;

create table if not exists public.profiles (
  id uuid primary key references auth.users(id) on delete cascade,
  email text not null unique,
  name text not null,
  role text not null check (role in ('account_manager','technical_review','hod','committee','country_admin','super_admin')),
  country text not null check (country in ('ZW','ZM','ALL')),
  department text,
  active boolean not null default true
);

create table if not exists public.tenders (
  id text primary key,
  source text not null,
  source_id text not null,
  country text not null check (country in ('ZW','ZM','ALL')),
  status text not null check (status in ('live','closed','award')),
  payload jsonb not null check (jsonb_typeof(payload) = 'object'),
  details jsonb,
  scraped_at timestamptz not null default now(),
  unique (source, source_id)
);
create index if not exists tenders_country_status_idx on public.tenders(country,status);

create table if not exists public.procurement_plans (
  id text primary key,
  country text not null check (country in ('ZW','ZM','ALL')),
  payload jsonb not null,
  details jsonb,
  scraped_at timestamptz not null default now()
);

-- Preserve the dashboard's existing JSON contracts; keep ownership and country
-- independently indexed for database-enforced access control.
create table if not exists public.app_records (
  kind text not null check (kind in ('assignment','pipeline','review','workflow','folder','document','partner','registration','renewal','lesson')),
  id text not null,
  country text not null check (country in ('ZW','ZM','ALL')),
  owner_email text,
  payload jsonb not null check (jsonb_typeof(payload) = 'object'),
  updated_at timestamptz not null default now(),
  primary key (kind,id)
);
create index if not exists app_records_access_idx on public.app_records(kind,country,owner_email);

create table if not exists public.scrape_requests (
  id uuid primary key default gen_random_uuid(),
  requested_by uuid references auth.users(id),
  pages integer not null default 5 check (pages between 1 and 50),
  status text not null default 'pending' check (status in ('pending','running','completed','failed')),
  message text,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create or replace function public.atcis_profile() returns public.profiles
language sql stable security definer set search_path = '' as $$
  select p from public.profiles p where p.id = auth.uid() and p.active;
$$;

create or replace function public.atcis_country_allowed(target text) returns boolean
language sql stable security definer set search_path = '' as $$
  select exists(select 1 from public.profiles p where p.id=auth.uid() and p.active
    and (p.country='ALL' or p.country=target or target='ALL'));
$$;

alter table public.profiles enable row level security;
alter table public.tenders enable row level security;
alter table public.procurement_plans enable row level security;
alter table public.app_records enable row level security;
alter table public.scrape_requests enable row level security;
create policy profiles_read on public.profiles for select to authenticated
  using (public.atcis_country_allowed(country));
create policy tenders_read on public.tenders for select to authenticated
  using (public.atcis_country_allowed(country));
create policy plans_read on public.procurement_plans for select to authenticated
  using (public.atcis_country_allowed(country));
create policy records_read on public.app_records for select to authenticated using (
  public.atcis_country_allowed(country) and (
    kind not in ('assignment','pipeline','review')
    or lower(owner_email)=lower((public.atcis_profile()).email)
    or (public.atcis_profile()).role in ('hod','technical_review','committee','country_admin','super_admin')
  )
);
create policy requests_read on public.scrape_requests for select to authenticated
  using (requested_by=auth.uid() or (public.atcis_profile()).role='super_admin');
create policy requests_insert on public.scrape_requests for insert to authenticated
  with check (requested_by=auth.uid() and status='pending' and (public.atcis_profile()).role='super_admin');

-- No direct client writes to records. This function validates authority and
-- preserves protected fields, including assignment ownership and approval state.
create or replace function public.save_app_record(p_kind text, p_id text, p_payload jsonb, p_delete boolean default false)
returns jsonb language plpgsql security definer set search_path = '' as $$
declare
  actor public.profiles := public.atcis_profile();
  old public.app_records;
  target_country text;
  target_owner text;
  data jsonb := p_payload;
  manager boolean;
begin
  if actor.id is null then raise exception 'Authentication required' using errcode='42501'; end if;
  if p_id is null or length(p_id)=0 or jsonb_typeof(data) <> 'object' then raise exception 'Invalid record'; end if;
  select * into old from public.app_records where kind=p_kind and id=p_id for update;
  manager := actor.role in ('hod','country_admin','super_admin');
  target_country := coalesce(data->>'countryCode',data->>'country',data->>'jurisdiction',old.country,actor.country);
  if target_country not in ('ZW','ZM','ALL') then raise exception 'Invalid country'; end if;
  if not public.atcis_country_allowed(target_country) or (old.id is not null and not public.atcis_country_allowed(old.country)) then
    raise exception 'Country access denied' using errcode='42501';
  end if;
  if p_kind='pipeline' then
    target_owner := lower(p_id);
    if target_owner<>lower(actor.email) then raise exception 'Only the board owner can save a pipeline' using errcode='42501'; end if;
    target_country := actor.country;
  elsif p_kind='assignment' then
    if not manager then
      if old.id is null or lower(old.owner_email)<>lower(actor.email) or p_delete then raise exception 'Assignment access denied' using errcode='42501'; end if;
      data := old.payload || jsonb_strip_nulls(jsonb_build_object('status',data->'status','progressPercentage',data->'progressPercentage','addedToPipeline',data->'addedToPipeline','pipelineTaskId',data->'pipelineTaskId'));
      target_country := old.country;
    else
      data := data || jsonb_build_object('assignedByHodName',actor.name);
    end if;
    target_owner := lower(data->>'assignedToEmail');
    if not exists(select 1 from public.profiles p where lower(p.email)=target_owner and p.active and p.role='account_manager' and (p.country=target_country or p.country='ALL')) then
      raise exception 'Choose an active account manager in this country';
    end if;
  elsif p_kind='review' then
    raise exception 'Use the review workflow operation' using errcode='42501';
  elsif p_kind='workflow' then
    if actor.role<>'super_admin' then raise exception 'Administrator required' using errcode='42501'; end if;
    target_country := 'ALL';
  elsif p_kind in ('folder','document','registration','renewal') then
    if actor.role not in ('country_admin','super_admin') or (target_country='ALL' and actor.role<>'super_admin') then
      raise exception 'Administrator required' using errcode='42501';
    end if;
  elsif p_kind in ('partner','lesson') then
    if not manager then raise exception 'Manager required' using errcode='42501'; end if;
  else raise exception 'Unknown record type';
  end if;
  if p_delete then
    delete from public.app_records where kind=p_kind and id=p_id;
    if p_kind='folder' then
      delete from public.app_records where kind='document' and payload->>'folderId'=p_id and public.atcis_country_allowed(country);
    end if;
    return jsonb_build_object('deleted',true);
  end if;
  data := data || jsonb_build_object('id',p_id);
  insert into public.app_records(kind,id,country,owner_email,payload) values(p_kind,p_id,target_country,target_owner,data)
    on conflict(kind,id) do update set country=excluded.country,owner_email=excluded.owner_email,payload=excluded.payload,updated_at=now();
  return data;
end;
$$;

create or replace function public.review_action(p_action text,p_id text,p_payload jsonb default '{}'::jsonb,p_step_id text default null,p_comments text default null)
returns jsonb language plpgsql security definer set search_path = '' as $$
declare
  actor public.profiles := public.atcis_profile();
  old public.app_records;
  data jsonb;
  steps jsonb;
  idx integer;
  step jsonb;
begin
  if actor.id is null then raise exception 'Authentication required' using errcode='42501'; end if;
  select * into old from public.app_records where kind='review' and id=p_id for update;
  if p_action='submit_checklist' then
    if not public.atcis_country_allowed(p_payload->>'countryCode') or actor.role not in ('account_manager','hod','country_admin','super_admin') then raise exception 'Submission denied' using errcode='42501'; end if;
    if old.id is not null and (lower(old.owner_email)<>lower(actor.email) or old.payload->>'overallStatus'<>'Revision Requested') then raise exception 'Submission already exists'; end if;
    select jsonb_agg(jsonb_build_object('stepId',s->>'id','stepName',s->>'name','requiredRole',s->>'requiredRole','status',case when n=1 then 'Pending' else 'Waiting' end) order by n)
      into steps from jsonb_array_elements((select payload->'steps' from public.app_records where kind='workflow' and id='default')) with ordinality as x(s,n);
    if steps is null then raise exception 'Configure the approval workflow first'; end if;
    data := p_payload || jsonb_build_object('id',p_id,'submittedByEmail',actor.email,'submittedByName',actor.name,'submittedAt',now(),'currentStepIndex',0,'overallStatus','In Review','approvalSteps',steps);
    insert into public.app_records(kind,id,country,owner_email,payload) values('review',p_id,p_payload->>'countryCode',lower(actor.email),data)
      on conflict(kind,id) do update set payload=excluded.payload,updated_at=now();
    return data;
  end if;
  if old.id is null or not public.atcis_country_allowed(old.country) then raise exception 'Submission not found'; end if;
  data := old.payload;
  idx := (data->>'currentStepIndex')::integer;
  step := data->'approvalSteps'->idx;
  if step is null or step->>'stepId'<>p_step_id or step->>'status'<>'Pending' or data->>'overallStatus'<>'In Review' then raise exception 'This step is not pending'; end if;
  if actor.role<>step->>'requiredRole' and actor.role<>'super_admin' then raise exception 'Approver role required' using errcode='42501'; end if;
  if lower(old.owner_email)=lower(actor.email) then raise exception 'You cannot approve your own submission' using errcode='42501'; end if;
  if p_action not in ('approve_step','request_revision') then raise exception 'Unknown review action'; end if;
  step := step || jsonb_build_object('status',case when p_action='approve_step' then 'Approved' else 'Revision Requested' end,'approvedByName',actor.name,'approvedByEmail',actor.email,'approvedAt',now(),'comments',coalesce(p_comments,''));
  data := jsonb_set(data,array['approvalSteps',idx::text],step);
  if p_action='request_revision' then
    data := data || jsonb_build_object('overallStatus','Revision Requested');
  else
    idx := idx+1;
    data := data || jsonb_build_object('currentStepIndex',idx);
    if idx=jsonb_array_length(data->'approvalSteps') then
      data := data || jsonb_build_object('overallStatus','Approved for Submission');
    else
      data := jsonb_set(data,array['approvalSteps',idx::text,'status'],'"Pending"'::jsonb);
    end if;
  end if;
  update public.app_records set payload=data,updated_at=now() where kind='review' and id=p_id;
  return data;
end;
$$;

create or replace function public.increment_document_download(p_id text) returns void
language plpgsql security definer set search_path = '' as $$
begin
  update public.app_records set payload=jsonb_set(payload,'{downloadsCount}',to_jsonb(coalesce((payload->>'downloadsCount')::integer,0)+1))
    where kind='document' and id=p_id and public.atcis_country_allowed(country);
  if not found then raise exception 'Document not found'; end if;
end;
$$;

revoke all on public.profiles,public.tenders,public.procurement_plans,public.app_records,public.scrape_requests from anon,authenticated;
grant select on public.profiles,public.tenders,public.procurement_plans,public.app_records,public.scrape_requests to authenticated;
grant insert(requested_by,pages) on public.scrape_requests to authenticated;
grant all on public.profiles,public.tenders,public.procurement_plans,public.app_records,public.scrape_requests to service_role;
revoke all on function public.atcis_profile(),public.atcis_country_allowed(text),public.save_app_record(text,text,jsonb,boolean),public.review_action(text,text,jsonb,text,text),public.increment_document_download(text) from public;
grant execute on function public.atcis_profile(),public.atcis_country_allowed(text),public.save_app_record(text,text,jsonb,boolean),public.review_action(text,text,jsonb,text,text),public.increment_document_download(text) to authenticated;

-- Dedicated scraper identity: only Admin API can assign app_metadata.
create or replace function public.atcis_is_scraper() returns boolean
language sql stable set search_path = '' as $$
  select coalesce((auth.jwt()->'app_metadata'->>'atcis_scraper')::boolean,false);
$$;
revoke all on function public.atcis_is_scraper() from public;
grant execute on function public.atcis_is_scraper() to authenticated;
grant insert,update on public.tenders,public.procurement_plans to authenticated;
grant update on public.scrape_requests to authenticated;
create policy scraper_tenders on public.tenders for all to authenticated using(public.atcis_is_scraper()) with check(public.atcis_is_scraper());
create policy scraper_plans on public.procurement_plans for all to authenticated using(public.atcis_is_scraper()) with check(public.atcis_is_scraper());
create policy scraper_requests_read on public.scrape_requests for select to authenticated using(public.atcis_is_scraper());
create policy scraper_requests_update on public.scrape_requests for update to authenticated using(public.atcis_is_scraper()) with check(public.atcis_is_scraper());

commit;
