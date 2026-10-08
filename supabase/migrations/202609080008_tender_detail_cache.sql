begin;

alter table public.tenders
  add column if not exists detail_cache_at timestamptz,
  add column if not exists detail_retry_at timestamptz,
  add column if not exists detail_refresh_token uuid,
  add column if not exists detail_refresh_until timestamptz;

create or replace function public.claim_tender_detail_refresh(p_id text)
returns uuid language plpgsql security definer set search_path = '' as $$
declare token uuid := gen_random_uuid(); claimed uuid;
begin
  update public.tenders
    set detail_refresh_token = token, detail_refresh_until = now() + interval '90 seconds'
    where id = p_id
      and (detail_cache_at is null or detail_cache_at < now() - interval '1 hour' or scraped_at > detail_cache_at)
      and (detail_retry_at is null or detail_retry_at <= now())
      and (detail_refresh_until is null or detail_refresh_until <= now())
    returning detail_refresh_token into claimed;
  return claimed;
end;
$$;

create or replace function public.finish_tender_detail_refresh(p_id text, p_token uuid, p_patch jsonb)
returns boolean language plpgsql security definer set search_path = '' as $$
declare changed integer;
begin
  if p_patch is not null and jsonb_typeof(p_patch) <> 'object' then
    raise exception 'Tender details must be an object';
  end if;
  update public.tenders set
    details = case when p_patch is null then details else coalesce(details, '{}'::jsonb) || p_patch end,
    detail_cache_at = case when p_patch is null then detail_cache_at else now() end,
    detail_retry_at = case when p_patch is null then now() + interval '5 minutes' else null end,
    detail_refresh_token = null,
    detail_refresh_until = null
  where id = p_id and detail_refresh_token = p_token;
  get diagnostics changed = row_count;
  return changed = 1;
end;
$$;

revoke all on function public.claim_tender_detail_refresh(text) from public, anon, authenticated;
revoke all on function public.finish_tender_detail_refresh(text, uuid, jsonb) from public, anon, authenticated;
grant execute on function public.claim_tender_detail_refresh(text) to service_role;
grant execute on function public.finish_tender_detail_refresh(text, uuid, jsonb) to service_role;

notify pgrst, 'reload schema';
commit;
