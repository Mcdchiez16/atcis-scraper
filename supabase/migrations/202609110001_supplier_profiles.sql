begin;

alter table public.app_records drop constraint if exists app_records_kind_check;
alter table public.app_records add constraint app_records_kind_check check (
  kind in ('assignment','pipeline','review','workflow','folder','document','partner','supplier','registration','renewal','lesson')
);

create or replace function public.save_app_record(p_kind text, p_id text, p_payload jsonb, p_delete boolean default false)
returns jsonb language plpgsql security definer set search_path = '' as $$
declare
  actor public.profiles := public.atcis_profile();
  old public.app_records;
  target_country text;
  target_owner text;
  data jsonb := p_payload;
  manager boolean;
  record_exists boolean;
  pipeline_board jsonb;
  pipeline_task jsonb;
begin
  if actor.id is null then raise exception 'Authentication required' using errcode='42501'; end if;
  if p_id is null or length(p_id)=0 or jsonb_typeof(data) <> 'object' then raise exception 'Invalid record'; end if;
  select * into old from public.app_records where kind=p_kind and id=p_id for update;
  record_exists := found;
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
    if target_country not in ('ZW','ZM') then raise exception 'An assignment requires a specific country'; end if;
    if not manager then
      if not record_exists or lower(old.owner_email)<>lower(actor.email) or p_delete then raise exception 'Assignment access denied' using errcode='42501'; end if;
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
    if jsonb_typeof(data->'steps') is distinct from 'array' or jsonb_array_length(data->'steps')=0 then raise exception 'A workflow needs at least one step'; end if;
    if exists(select 1 from jsonb_array_elements(data->'steps') s where coalesce(s->>'id','')='' or coalesce(s->>'name','')='' or coalesce(s->>'requiredRole','') not in ('hod','technical_review','committee','country_admin','super_admin')) then raise exception 'Invalid workflow step'; end if;
    if (select count(*)<>count(distinct s->>'id') from jsonb_array_elements(data->'steps') s) then raise exception 'Duplicate workflow step'; end if;
    target_country := 'ALL';
  elsif p_kind in ('folder','document','registration','renewal') then
    if actor.role not in ('country_admin','super_admin') or ((target_country='ALL' or old.country='ALL') and actor.role<>'super_admin') then
      raise exception 'Administrator required' using errcode='42501';
    end if;
  elsif p_kind='supplier' then
    if target_country not in ('ZW','ZM') then raise exception 'A supplier requires a specific country'; end if;
    if record_exists and lower(coalesce(old.owner_email,''))<>lower(actor.email) and not manager then
      raise exception 'Only the supplier creator or a manager can update this profile' using errcode='42501';
    end if;
    target_owner := lower(coalesce(old.owner_email,actor.email));
    data := data || jsonb_build_object(
      'createdByEmail',target_owner,
      'createdByName',coalesce(old.payload->>'createdByName',actor.name)
    );
    if not manager then data := data || jsonb_build_object('verificationStatus','Pending review'); end if;
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
  if p_kind='document' and not exists(select 1 from public.app_records f where f.kind='folder' and f.id=data->>'folderId' and (f.country='ALL' or f.country=target_country)) then raise exception 'Document folder is missing or belongs to another country'; end if;
  data := data || jsonb_build_object('id',p_id);
  insert into public.app_records(kind,id,country,owner_email,payload) values(p_kind,p_id,target_country,target_owner,data)
    on conflict(kind,id) do update set country=excluded.country,owner_email=excluded.owner_email,payload=excluded.payload,updated_at=now();
  if p_kind='assignment' and manager and coalesce((data->>'addedToPipeline')::boolean,false) then
    perform pg_advisory_xact_lock(hashtextextended(target_owner,0));
    select payload->'board' into pipeline_board from public.app_records where kind='pipeline' and id=target_owner for update;
    pipeline_board := coalesce(pipeline_board,'{}'::jsonb);
    pipeline_task := jsonb_build_object(
      'id',coalesce(data->>'pipelineTaskId','assigned-task-'||p_id),
      'refNo',data->>'tenderRef','countryCode',target_country,'title',data->>'tenderTitle',
      'entity',data->>'entity','estimatedValue',data->'estimatedValue','description',coalesce(data->>'instructions',''),
      'priority',case when data->>'priority'='Urgent' then 'High' else coalesce(data->>'priority','Medium') end,
      'dueDate',coalesce(data->>'dueDate',''),'progress',coalesce(data->'progressPercentage','0'::jsonb),
      'owner',jsonb_build_object('name',data->>'assignedToName','tone',''),
      'team','Compliance & Legal','insights','[]'::jsonb);
    pipeline_board := jsonb_set(
      pipeline_board,
      '{new}',
      jsonb_build_array(pipeline_task) || coalesce(
        (select jsonb_agg(task) from jsonb_array_elements(coalesce(pipeline_board->'new','[]'::jsonb)) task
          where task->>'id' <> pipeline_task->>'id'),
        '[]'::jsonb
      )
    );
    insert into public.app_records(kind,id,country,owner_email,payload) values('pipeline',target_owner,target_country,target_owner,jsonb_build_object('id',target_owner,'board',pipeline_board))
      on conflict(kind,id) do update set payload=excluded.payload,updated_at=now();
  end if;
  return data;
end;
$$;

notify pgrst, 'reload schema';
commit;
