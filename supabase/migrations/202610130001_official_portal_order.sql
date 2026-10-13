begin;

-- Keep all sources visible while leading each national directory with its
-- official portal: ZPPA for Zambia and PRAZ e-GP for Zimbabwe. Country RLS
-- prevents one country's national portal from entering the other's results.
create index if not exists tenders_catalog_official_newest_idx
  on public.tenders(country,(catalog_portal in ('PRAZ','ZPPA')) desc,catalog_published_at desc nulls last,id)
  where status = 'live';

create or replace function public.tender_catalog_page(
  p_country text default 'ALL', p_portal text default 'ALL', p_category text default 'ALL',
  p_status text default 'ALL', p_confidence text default 'ALL', p_focus boolean default false,
  p_query text default '', p_sort text default 'NEWEST', p_page integer default 1, p_size integer default 50
) returns jsonb language plpgsql stable security invoker set search_path = '' as $$
declare
  filter_sql text;
  order_sql text;
  total bigint;
  actual_page integer;
  result jsonb;
  updated timestamptz;
  query_pattern text;
  category_pattern text;
begin
  if auth.uid() is null or not exists(select 1 from public.profiles where id = auth.uid() and active) then
    raise exception 'Authentication required' using errcode = '42501';
  end if;
  if p_country is null or p_country not in ('ALL','ZW','ZM') or p_size is null or p_size not between 1 and 100
     or p_page is null or p_page not between 1 and 100000 or length(p_query) > 300 or length(p_category) > 100
     or p_sort is null or p_sort not in ('NEWEST','CLOSING_SOON','PRIORITY','HIGHEST_SCORE','PORTAL_ORDER') then
    raise exception 'Invalid catalogue parameters' using errcode = '22023';
  end if;
  query_pattern := '%' || replace(replace(replace(lower(coalesce(p_query,'')), E'\\', E'\\\\'), '%', E'\\%'), '_', E'\\_') || '%';
  category_pattern := '%' || replace(replace(replace(lower(coalesce(p_category,'')), E'\\', E'\\\\'), '%', E'\\%'), '_', E'\\_') || '%';
  filter_sql := ' from public.tenders where status = ''live''
    and (catalog_closes_at is null or catalog_closes_at >= now())
    and ($1 = ''ALL'' or country in ($1,''ALL''))
    and ($2 = ''ALL'' or catalog_portal = $2)
    and ($3 = ''ALL'' or lower(catalog_sector) like $10 or lower(catalog_category) like $10)
    and ($4 = ''ALL'' or ($4 = ''Closing Soon'' and catalog_closes_at > now() and catalog_closes_at <= now()+interval ''3 days'')
         or ($4 = ''Open'' and (catalog_closes_at is null or catalog_closes_at > now()+interval ''3 days'')))
    and ($5 = ''ALL'' or ($5 = ''HIGH'' and catalog_score >= 88) or ($5 = ''TOP'' and catalog_score >= 92))
    and (not $6 or catalog_focus) and catalog_search like $7';
  order_sql := case p_sort
    when 'NEWEST' then '(catalog_portal in (''PRAZ'',''ZPPA'')) desc,catalog_published_at desc nulls last,id'
    when 'CLOSING_SOON' then 'catalog_closes_at asc nulls last,id'
    when 'HIGHEST_SCORE' then 'catalog_score desc,catalog_published_at desc nulls last,id'
    when 'PORTAL_ORDER' then 'catalog_site_order asc nulls last,catalog_published_at desc nulls last,id'
    else 'catalog_is_award,catalog_portal_rank,catalog_site_order asc nulls last,catalog_closes_at asc nulls last,id' end;
  execute 'select count(*),max(scraped_at)' || filter_sql into total,updated
    using p_country,p_portal,p_category,p_status,p_confidence,p_focus,query_pattern,p_size,0,category_pattern;
  actual_page := least(p_page,greatest(1,ceil(total::numeric/p_size)::integer));
  execute 'select coalesce(jsonb_agg(item),''[]''::jsonb) from (select catalog_summary || jsonb_build_object(
      ''daysRemaining'',case when catalog_closes_at is null then 0 else greatest(0,ceil(extract(epoch from catalog_closes_at-now())/86400)) end,
      ''status'',case when catalog_closes_at > now() and catalog_closes_at <= now()+interval ''3 days'' then ''Closing Soon'' else ''Open'' end
    ) as item' || filter_sql || ' order by ' || order_sql || ' limit $8 offset $9) page_rows' into result
    using p_country,p_portal,p_category,p_status,p_confidence,p_focus,query_pattern,p_size,(actual_page-1)*p_size,category_pattern;
  return jsonb_build_object('data',result,'total',total,'page',actual_page,'pageSize',p_size,'updatedAt',updated,'refreshing',false);
end;
$$;

revoke all on function public.tender_catalog_page(text,text,text,text,text,boolean,text,text,integer,integer) from public,anon;
grant execute on function public.tender_catalog_page(text,text,text,text,text,boolean,text,text,integer,integer) to authenticated;
notify pgrst, 'reload schema';
commit;
