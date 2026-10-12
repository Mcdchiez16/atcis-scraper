begin;

-- Requires 202609220001_tender_ai_classification_cache.sql first.
-- Additive migration: existing payloads, attachments and RLS policies are retained.
alter table public.tenders
  add column if not exists catalog_summary jsonb not null default '{}',
  add column if not exists catalog_published_at timestamptz,
  add column if not exists catalog_closes_at timestamptz,
  add column if not exists catalog_sector text not null default 'Other',
  add column if not exists catalog_category text not null default 'Needs Classification Review',
  add column if not exists catalog_score integer not null default 0,
  add column if not exists catalog_focus boolean not null default false,
  add column if not exists catalog_portal text not null default '',
  add column if not exists catalog_portal_rank integer not null default 7,
  add column if not exists catalog_site_order integer,
  add column if not exists catalog_is_award boolean not null default false,
  add column if not exists catalog_search text not null default '';

-- Portal dates without a time expire at the END of that day in UTC+02:00.
-- Invalid/missing dates remain unknown, never silently invented or closed.
create or replace function public.catalog_date(value text, deadline boolean default false)
returns timestamptz language plpgsql stable set search_path = '' as $$
begin
  if value is null or btrim(value) = '' then return null; end if;
  if value ~ '^\d{4}-\d{2}-\d{2}$' then
    return (value || case when deadline then 'T23:59:59.999+02:00' else 'T00:00:00+02:00' end)::timestamptz;
  end if;
  if value !~ '^\d{4}-\d{2}-\d{2}[T ]' then return null; end if;
  if value !~ '(Z|[+-]\d{2}:?\d{2})$' then value := value || '+02:00'; end if;
  return value::timestamptz;
exception when invalid_datetime_format or datetime_field_overflow then return null;
end;
$$;

create or replace function public.prepare_tender_catalogue()
returns trigger language plpgsql set search_path = '' as $$
declare
  p jsonb := new.payload;
  decision jsonb;
  classification_source text := 'rules';
  evidence jsonb;
  needs_review boolean;
  title text := coalesce(nullif(p->>'title',''), 'Procurement Notice');
  reference text := coalesce(nullif(p->>'referenceNumber',''), nullif(p->>'refNo',''), nullif(p->>'tenderId',''), new.source_id);
  buyer text := coalesce(nullif(p->>'procuringEntity',''), nullif(p->>'entity',''), 'Procuring Entity');
  score_text text;
begin
  -- An edited notice must be classified again. Unchanged scraper refreshes do
  -- not invalidate saved AI results. Manual decisions remain authoritative.
  if TG_OP = 'UPDATE' and
     jsonb_build_array(old.payload->'title',old.payload->'description',old.payload->'scope',old.payload->'lineItems')
       is distinct from jsonb_build_array(p->'title',p->'description',p->'scope',p->'lineItems') then
    new.ai_classification := null;
    new.ai_classification_hash := null;
    new.ai_classification_version := null;
    new.ai_classification_retry_at := null;
    new.ai_classification_refresh_until := null;
    new.ai_classification_refresh_token := null;
  end if;
  if jsonb_typeof(new.classification_override) = 'object' then
    decision := new.classification_override;
    classification_source := 'manual';
  elsif new.ai_classification_version = 'tender-sector-v2'
        and jsonb_typeof(new.ai_classification) = 'object' then
    decision := new.ai_classification;
    classification_source := 'ai';
  end if;
  new.catalog_sector := coalesce(decision->>'sector', p->>'sector', 'Other');
  if new.catalog_sector not in ('ICT & Software','Healthcare & Medical','Electrical & Energy',
      'Civil & Infrastructure','General Goods & Consumables','Services & Logistics','Other') then
    new.catalog_sector := 'Other';
  end if;
  new.catalog_category := coalesce(decision->>'subcategory',p->>'category','Needs Classification Review');
  score_text := coalesce(decision->>'confidence',p->>'classificationConfidence',p->>'aiScore','0');
  new.catalog_score := case when score_text ~ '^\d{1,3}(\.\d+)?$'
    then least(100,greatest(0,round(score_text::numeric)::integer)) else 0 end;
  needs_review := new.catalog_score < 80 or new.catalog_sector = 'Other'
    or (decision is null and coalesce(p->>'needsClassificationReview','false') = 'true');
  new.catalog_focus := new.catalog_sector in ('ICT & Software','Healthcare & Medical') and not needs_review;
  evidence := coalesce(decision->'evidence',p->'classificationEvidence','[]'::jsonb);
  if jsonb_typeof(evidence) <> 'array' then evidence := '[]'::jsonb; end if;
  new.catalog_published_at := public.catalog_date(p->>'publishDate');
  new.catalog_closes_at := public.catalog_date(p->>'closingDate',true);
  new.catalog_portal := case
    when new.source ilike '%online%' then 'OnlineTenders'
    when new.source ilike '%ungm%' or new.source ilike '%unprocurement%' then 'UNGM'
    when new.source ilike '%world%bank%' then 'World Bank'
    when new.source ilike '%afdb%' then 'AfDB'
    when new.source ilike '%gozambia%' then 'GoZambiaJobs'
    when new.source ilike '%zppa%' then 'ZPPA'
    when new.source ilike '%praz%' then 'PRAZ'
    else coalesce(p->>'sourcePortal',new.source) end;
  new.catalog_portal_rank := case new.catalog_portal
    when 'PRAZ' then 1 when 'OnlineTenders' then 2 when 'UNGM' then 3
    when 'World Bank' then 4 when 'ZPPA' then 5 when 'AfDB' then 6 else 7 end;
  new.catalog_site_order := case when p->>'siteOrder' ~ '^\d{1,8}$' then (p->>'siteOrder')::integer end;
  new.catalog_is_award := title ilike '%contract award%' or title ilike '%attribution%';
  new.catalog_search := lower(concat_ws(' ',title,reference,buyer,new.source_id,new.catalog_portal,new.catalog_sector,new.catalog_category));
  new.catalog_summary := jsonb_build_object(
    'id',new.id,'tenderId',new.source_id,'refNo',reference,'title',title,'procuringEntity',buyer,
    'countryCode',new.country,'countryName',case new.country when 'ZM' then 'Zambia' when 'ZW' then 'Zimbabwe' else 'Regional / Multilateral' end,
    'category',new.catalog_category,'sector',new.catalog_sector,
    'estimatedValue',case when jsonb_typeof(p->'estimatedValue') = 'number' then p->'estimatedValue'
      when jsonb_typeof(p->'contractValue') = 'number' then p->'contractValue' else '0'::jsonb end,
    'currency',coalesce(p->>'currency','USD'),
    'publishDate',coalesce(p->>'publishDate',''),'closingDate',coalesce(p->>'closingDate',''),
    'aiScore',new.catalog_score,'classificationSource',classification_source,
    'classificationConfidence',new.catalog_score,'classificationEvidence',evidence,
    'classificationReason',coalesce(decision->>'reason',p->>'classificationReason','Saved source classification'),
    'focusEligible',new.catalog_focus,'needsClassificationReview',needs_review,
    'sourcePortal',case new.catalog_portal when 'ZPPA' then 'ZPPA e-Procurement' when 'PRAZ' then 'PRAZ e-GP'
      when 'UNGM' then 'UN Global Marketplace (UNGM)' when 'OnlineTenders' then 'OnlineTenders ' || case new.country when 'ZM' then 'Zambia' else 'Zimbabwe' end else new.catalog_portal end,
    'portalUrl',coalesce(nullif(p->>'detailsUrl',''),nullif(p->>'sourceUrl',''),nullif(p->>'portalUrl',''),
      case new.catalog_portal
        when 'PRAZ' then 'https://egp.praz.org.zw/Indexes/viewLiveTenderDetails/' || new.source_id
        when 'ZPPA' then 'https://eprocure.zppa.org.zm/epps/cft/prepareViewCfTWS.do?resourceId=' || new.source_id
        when 'UNGM' then 'https://www.ungm.org/Public/Notice'
        when 'World Bank' then 'https://projects.worldbank.org/en/projects-operations/procurement-notices'
        when 'AfDB' then 'https://www.afdb.org/en/projects-and-operations/procurement' else null end),
    'description',left(coalesce(p->>'description',p->>'scope',''),1000),
    'procurementMethod',coalesce(p->>'procurementMethod',p->>'procurementType'),
    'procurementType',p->>'procurementType','siteOrder',new.catalog_site_order
  );
  return new;
end;
$$;

drop trigger if exists prepare_tender_catalogue on public.tenders;
create trigger prepare_tender_catalogue before insert or update of payload,country,source,source_id,
  ai_classification,classification_override on public.tenders
  for each row execute function public.prepare_tender_catalogue();

-- Backfill summaries without touching scrape timestamps or inserting notices.
update public.tenders set payload = payload;

create index if not exists tenders_catalog_newest_idx
  on public.tenders(catalog_published_at desc nulls last,id) where status = 'live';
create index if not exists tenders_catalog_country_newest_idx
  on public.tenders(country,catalog_published_at desc nulls last,id) where status = 'live';
create index if not exists tenders_catalog_portal_newest_idx
  on public.tenders(catalog_portal,catalog_published_at desc nulls last,id) where status = 'live';
create index if not exists tenders_catalog_deadline_idx
  on public.tenders(catalog_closes_at,id) where status = 'live';
create index if not exists tenders_catalog_review_idx
  on public.tenders(ai_classification_retry_at nulls first,catalog_published_at desc nulls last,id)
  where status = 'live' and classification_override is null and ai_classification is null
    and catalog_summary->>'needsClassificationReview' = 'true';

-- Runs as the caller: country filters can only narrow rows allowed by RLS.
-- No secret/service-role client is used for this RPC in the web application.
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
  -- Literal substring search: user input never becomes SQL or PostgREST syntax.
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
    when 'NEWEST' then 'catalog_published_at desc nulls last,id'
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

revoke all on function public.prepare_tender_catalogue() from public,anon,authenticated;
revoke all on function public.catalog_date(text,boolean) from public,anon;
grant execute on function public.catalog_date(text,boolean) to authenticated,service_role;
revoke all on function public.tender_catalog_page(text,text,text,text,text,boolean,text,text,integer,integer) from public,anon;
grant execute on function public.tender_catalog_page(text,text,text,text,text,boolean,text,text,integer,integer) to authenticated;
notify pgrst, 'reload schema';
commit;
