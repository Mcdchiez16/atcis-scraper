begin;

-- Preserve the checklist's own category when its evidence is snapshotted for
-- review. Reference documents remain a distinct, clearly labelled group.
create or replace function public.categorize_review_package_file()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
  requirement jsonb;
begin
  if new.category = 'Checklist' or new.id like 'checklist-%' then
    select requirement_item.value into requirement
    from public.app_records r
    cross join lateral jsonb_array_elements(coalesce(r.payload->'requirements', '[]'::jsonb))
      as requirement_item(value)
    where r.kind = 'review'
      and r.id = new.review_id
      and lower(btrim(requirement_item.value->>'item')) = lower(btrim(new.name))
    limit 1;
    new.category := coalesce(nullif(btrim(requirement->>'category'), ''), 'Uncategorized');
  elsif new.category = 'Reference' or new.id like 'reference-%' then
    new.category := 'Reference Documents';
  end if;
  return new;
end;
$$;

drop trigger if exists categorize_review_package_file on public.review_package_files;
create trigger categorize_review_package_file
before insert or update of category, name on public.review_package_files
for each row execute function public.categorize_review_package_file();

-- Correct packages submitted before this migration.
update public.review_package_files f
set category = coalesce((
  select nullif(btrim(requirement_item.value->>'category'), '')
  from public.app_records r
  cross join lateral jsonb_array_elements(coalesce(r.payload->'requirements', '[]'::jsonb))
    as requirement_item(value)
  where r.kind = 'review'
    and r.id = f.review_id
    and lower(btrim(requirement_item.value->>'item')) = lower(btrim(f.name))
  limit 1
), 'Uncategorized')
where f.category = 'Checklist' or f.id like 'checklist-%';

update public.review_package_files
set category = 'Uncategorized'
where category = 'Checklist' or (id like 'checklist-%' and btrim(category) = '');

update public.review_package_files
set category = 'Reference Documents'
where category = 'Reference' or id like 'reference-%';

-- Keep the review payload used by the UI aligned with the immutable package rows.
update public.app_records r
set payload = jsonb_set(
  r.payload,
  '{checklist}',
  coalesce((
    select jsonb_agg(
      document.value || jsonb_build_object('category', coalesce(file.category, document.value->>'category'))
      order by document.ordinality
    )
    from jsonb_array_elements(r.payload->'checklist') with ordinality document(value, ordinality)
    left join public.review_package_files file
      on file.review_id = r.id and file.id = document.value->>'id'
  ), '[]'::jsonb)
)
where r.kind = 'review'
  and jsonb_typeof(r.payload->'checklist') = 'array';

revoke all on function public.categorize_review_package_file() from public, anon, authenticated;

commit;
