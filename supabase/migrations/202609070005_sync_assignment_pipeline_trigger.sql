begin;

create or replace function public.sync_assignment_pipeline() returns trigger
language plpgsql security definer set search_path = '' as $$
declare
  board jsonb;
  task jsonb;
  task_id text;
begin
  if new.kind <> 'assignment' or not coalesce((new.payload->>'addedToPipeline')::boolean, false) then
    return new;
  end if;
  task_id := coalesce(new.payload->>'pipelineTaskId', 'assigned-task-' || new.id);
  perform pg_advisory_xact_lock(hashtextextended(lower(new.owner_email), 0));
  select payload->'board' into board from public.app_records
    where kind='pipeline' and id=lower(new.owner_email) for update;
  board := coalesce(board, '{}'::jsonb);
  task := jsonb_build_object(
    'id',task_id,'refNo',new.payload->>'tenderRef','countryCode',new.country,
    'title',new.payload->>'tenderTitle','entity',new.payload->>'entity',
    'estimatedValue',new.payload->'estimatedValue','description',coalesce(new.payload->>'instructions',''),
    'priority',case when new.payload->>'priority'='Urgent' then 'High' else coalesce(new.payload->>'priority','Medium') end,
    'dueDate',coalesce(new.payload->>'dueDate',''),'progress',coalesce(new.payload->'progressPercentage','0'::jsonb),
    'owner',jsonb_build_object('name',new.payload->>'assignedToName','tone',''),
    'team','Compliance & Legal','insights','[]'::jsonb
  );
  board := jsonb_set(
    board,'{new}',jsonb_build_array(task) || coalesce(
      (select jsonb_agg(item) from jsonb_array_elements(coalesce(board->'new','[]'::jsonb)) item
        where item->>'id' <> task_id),'[]'::jsonb)
  );
  insert into public.app_records(kind,id,country,owner_email,payload)
    values('pipeline',lower(new.owner_email),new.country,lower(new.owner_email),jsonb_build_object('id',lower(new.owner_email),'board',board))
  on conflict(kind,id) do update set country=excluded.country,owner_email=excluded.owner_email,
    payload=excluded.payload,updated_at=now();
  return new;
end;
$$;

drop trigger if exists assignment_pipeline_sync on public.app_records;
create trigger assignment_pipeline_sync after insert or update of payload,owner_email,country
on public.app_records for each row execute function public.sync_assignment_pipeline();
revoke all on function public.sync_assignment_pipeline() from public;

commit;
