begin;

alter table public.tenders
  add column if not exists ai_classification jsonb,
  add column if not exists ai_classification_hash text,
  add column if not exists ai_classification_version text,
  add column if not exists ai_classified_at timestamptz,
  add column if not exists ai_classification_retry_at timestamptz,
  add column if not exists ai_classification_refresh_until timestamptz,
  add column if not exists ai_classification_refresh_token uuid,
  add column if not exists classification_override jsonb;

create index if not exists tenders_ai_classification_refresh_idx
  on public.tenders(status, ai_classification_retry_at, ai_classification_refresh_until)
  where status = 'live' and classification_override is null;

create or replace function public.claim_tender_classification(
  p_id text,
  p_hash text,
  p_version text
) returns uuid
language plpgsql
security definer
set search_path = ''
as $$
declare
  token uuid := gen_random_uuid();
begin
  if auth.role() <> 'service_role' then
    raise exception 'Service role required' using errcode = '42501';
  end if;
  if p_id is null or length(p_id) > 500 or p_hash !~ '^[0-9a-f]{64}$' or length(p_version) not between 1 and 100 then
    raise exception 'Invalid classification claim';
  end if;

  update public.tenders
  set ai_classification_refresh_token = token,
      ai_classification_refresh_until = now() + interval '90 seconds'
  where id = p_id
    and status = 'live'
    and classification_override is null
    and (ai_classification_retry_at is null or ai_classification_retry_at <= now())
    and (ai_classification_refresh_until is null or ai_classification_refresh_until <= now())
    and (
      ai_classification is null
      or ai_classification_hash is distinct from p_hash
      or ai_classification_version is distinct from p_version
    );

  if not found then return null; end if;
  return token;
end;
$$;

create or replace function public.finish_tender_classification(
  p_id text,
  p_token uuid,
  p_hash text,
  p_version text,
  p_result jsonb
) returns boolean
language plpgsql
security definer
set search_path = ''
as $$
begin
  if auth.role() <> 'service_role' then
    raise exception 'Service role required' using errcode = '42501';
  end if;

  if p_result is not null and (
    jsonb_typeof(p_result) <> 'object'
    or not (p_result ? 'sector')
    or not (p_result ? 'confidence')
    or not (p_result ? 'evidence')
  ) then
    raise exception 'Invalid classification result';
  end if;

  if p_result is null then
    update public.tenders
    set ai_classification_retry_at = now() + interval '15 minutes',
        ai_classification_refresh_until = null,
        ai_classification_refresh_token = null
    where id = p_id and ai_classification_refresh_token = p_token;
  else
    update public.tenders
    set ai_classification = p_result,
        ai_classification_hash = p_hash,
        ai_classification_version = p_version,
        ai_classified_at = now(),
        ai_classification_retry_at = null,
        ai_classification_refresh_until = null,
        ai_classification_refresh_token = null
    where id = p_id and ai_classification_refresh_token = p_token;
  end if;

  return found;
end;
$$;

revoke all on function public.claim_tender_classification(text,text,text) from public, anon, authenticated;
revoke all on function public.finish_tender_classification(text,uuid,text,text,jsonb) from public, anon, authenticated;
grant execute on function public.claim_tender_classification(text,text,text) to service_role;
grant execute on function public.finish_tender_classification(text,uuid,text,text,jsonb) to service_role;

commit;
