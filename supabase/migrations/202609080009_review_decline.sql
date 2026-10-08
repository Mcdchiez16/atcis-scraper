begin;

-- Reviewers can decline with a reason; AMs can correct and resubmit declined packages.
create or replace function public.review_action(p_action text,p_id text,p_payload jsonb default '{}'::jsonb,p_step_id text default null,p_comments text default null)
returns jsonb language plpgsql security definer set search_path='' as $$
declare
  actor public.profiles := public.atcis_profile();
  c public.tender_checklists;
  previous public.app_records;
  data jsonb;
  steps jsonb;
  docs jsonb;
  idx integer;
  step jsonb;
  revision integer;
begin
  if actor.id is null then raise exception 'Authentication required' using errcode='42501'; end if;
  if coalesce(p_action,'') not in ('submit_checklist','approve_step','request_revision','decline_submission') then raise exception 'Unknown review action'; end if;
  -- Always lock checklist before review, including on resubmission.
  select * into c from public.tender_checklists where 'checklist-'||id::text=p_id for update;
  select * into previous from public.app_records where kind='review' and id=p_id for update;
  if p_action='submit_checklist' then
    if c.id is null or c.owner_id<>actor.id or not public.atcis_country_allowed(c.country)
      or actor.role not in ('account_manager','hod','country_admin','super_admin') then
      raise exception 'Submit your own saved tender checklist' using errcode='42501';
    end if;
    if previous.id is not null and (coalesce(previous.payload->>'overallStatus','') not in ('Revision Requested','Declined')
      or lower(previous.owner_email) is distinct from lower(actor.email)) then
      raise exception 'This checklist has already been submitted';
    end if;
    if coalesce(p_payload->>'revision','') is distinct from coalesce(previous.payload->>'revision','0') then
      raise exception 'This submission has changed. Refresh before submitting';
    end if;
    if exists(select 1 from jsonb_array_elements(c.items) i where i->>'status'='Mandatory'
      and not exists(select 1 from public.checklist_attachments a where a.checklist_id=c.id and a.item_id=i->>'id')) then
      raise exception 'Attach a document to every mandatory checklist item before submitting';
    end if;
    if not exists(select 1 from public.checklist_attachments where checklist_id=c.id) then
      raise exception 'Attach at least one checklist document before submitting';
    end if;
    if (select count(*) from public.tender_reference_documents where country=c.country and tender_key=c.tender_key and owner_id=c.owner_id)<3 then
      raise exception 'Upload three reference documents before submitting';
    end if;
    steps := '[{"stepId":"step-hod","stepName":"HOD Review","requiredRole":"hod","status":"Pending"},{"stepId":"step-tech","stepName":"Technical Review","requiredRole":"technical_review","status":"Waiting"},{"stepId":"step-committee","stepName":"Committee Review","requiredRole":"committee","status":"Waiting"}]'::jsonb;
    revision := coalesce((previous.payload->>'revision')::integer,0)+1;
    data := jsonb_build_object('id',p_id,'checklistId',c.id,'tenderKey',c.tender_key,
      'tenderRef',coalesce(nullif(left(btrim(p_payload->>'tenderRef'),500),''),c.tender_key),
      'tenderTitle',left(coalesce(p_payload->>'tenderTitle',c.tender_key),2000),
      'entity',left(coalesce(p_payload->>'entity',''),1000),'bidAmount',0,
      'countryCode',c.country,'submittedByEmail',actor.email,'submittedByName',actor.name,
      'submittedAt',now(),'currentStepIndex',0,'overallStatus','In Review',
      'approvalSteps',steps,'revision',revision,'checklistName',c.template_name,'requirements',c.items,
      'history',coalesce(previous.payload->'history','[]'::jsonb) ||
        case when previous.id is null then '[]'::jsonb else jsonb_build_array(jsonb_build_object(
          'revision',previous.payload->'revision','submittedAt',previous.payload->'submittedAt',
          'approvalSteps',previous.payload->'approvalSteps')) end);
    insert into public.app_records(kind,id,country,owner_email,payload)
      values('review',p_id,c.country,lower(actor.email),data)
      on conflict(kind,id) do update set payload=excluded.payload,updated_at=now();
    delete from public.review_package_files where review_id=p_id;
    insert into public.review_package_files(review_id,id,category,name,file_name,size,storage_bucket,storage_path)
      select p_id,'checklist-'||a.id::text,'Checklist',i->>'item',coalesce(a.original_file_name,a.name),a.size,a.storage_bucket,a.storage_path
      from public.checklist_attachments a cross join jsonb_array_elements(c.items) i
      where a.checklist_id=c.id and i->>'id'=a.item_id
      union all
      select p_id,'reference-'||d.id::text,'Reference',d.name,d.file_name,d.file_size,'reference-documents',d.storage_path
      from public.tender_reference_documents d where d.country=c.country and d.tender_key=c.tender_key and d.owner_id=c.owner_id;
    -- Hold source objects until the transaction commits and reject missing bytes.
    perform o.id from storage.objects o join public.review_package_files f
      on f.storage_bucket=o.bucket_id and f.storage_path=o.name where f.review_id=p_id for share of o;
    if exists(select 1 from public.review_package_files f where f.review_id=p_id and not exists(
      select 1 from storage.objects o where o.bucket_id=f.storage_bucket and o.name=f.storage_path
        and (o.metadata->>'size')::bigint=f.size)) then
      raise exception 'A document file is missing. Reattach it before submitting';
    end if;
    select jsonb_agg(jsonb_build_object('id',f.id,'name',f.name,'category',f.category,
      'fileName',f.file_name,'fileSize',f.size::text||' bytes','status','Pending Review') order by f.category,f.id)
      into docs from public.review_package_files f where f.review_id=p_id;
    data := data || jsonb_build_object('checklist',docs);
    update public.app_records set payload=data where kind='review' and id=p_id;
    return data;
  end if;
  if previous.id is null or not public.atcis_country_allowed(previous.country) then
    raise exception 'Submission not found' using errcode='42501';
  end if;
  if c.id is null or not exists(select 1 from public.review_package_files where review_id=p_id) then
    raise exception 'This legacy submission has no saved document package. Submit the checklist from the tender page';
  end if;
  data := previous.payload;
  if p_payload->>'revision' is distinct from data->>'revision' then
    raise exception 'This submission has been revised. Refresh and review the current documents';
  end if;
  idx := (data->>'currentStepIndex')::integer;
  step := data->'approvalSteps'->idx;
  if step is null or p_step_id is distinct from step->>'stepId' or step->>'status'<>'Pending'
    or data->>'overallStatus'<>'In Review' then raise exception 'This review step is no longer pending. Refresh the submission'; end if;
  if actor.role<>step->>'requiredRole' then raise exception 'The assigned reviewer role is required for this stage' using errcode='42501'; end if;
  if lower(previous.owner_email)=lower(actor.email) then raise exception 'You cannot approve your own submission' using errcode='42501'; end if;
  if p_action not in ('approve_step','request_revision','decline_submission') then raise exception 'Unknown review action'; end if;
  if p_action in ('request_revision','decline_submission') and coalesce(length(btrim(p_comments)),0)=0 then raise exception 'Explain the changes needed'; end if;
  step := step || jsonb_build_object('status',case when p_action='approve_step' then 'Approved' when p_action='decline_submission' then 'Declined' else 'Revision Requested' end,
    'approvedByName',actor.name,'approvedByEmail',actor.email,'approvedAt',now(),'comments',left(coalesce(p_comments,''),5000));
  data := jsonb_set(data,array['approvalSteps',idx::text],step);
  if p_action in ('request_revision','decline_submission') then
    data := data || jsonb_build_object('overallStatus',case when p_action='decline_submission' then 'Declined' else 'Revision Requested' end);
  else
    idx := idx+1;
    data := data || jsonb_build_object('currentStepIndex',idx);
    if idx=jsonb_array_length(data->'approvalSteps') then
      data := data || jsonb_build_object('overallStatus','Approved for Submission','approvedAt',now());
      select jsonb_agg(d || jsonb_build_object('status','Verified','verifiedDate',current_date))
        into docs from jsonb_array_elements(data->'checklist') d;
      data := data || jsonb_build_object('checklist',docs);
    else
      data := jsonb_set(data,array['approvalSteps',idx::text,'status'],'"Pending"'::jsonb);
    end if;
  end if;
  update public.app_records set payload=data,updated_at=now() where kind='review' and id=p_id;
  return data;
end;
$$;
revoke all on function public.review_action(text,text,jsonb,text,text) from public,anon;
grant execute on function public.review_action(text,text,jsonb,text,text) to authenticated;
notify pgrst, 'reload schema';
commit;
