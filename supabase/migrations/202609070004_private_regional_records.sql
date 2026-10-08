begin;
drop policy records_read on public.app_records;
create policy records_read on public.app_records for select to authenticated using (
  case when kind in ('assignment','pipeline','review') then
    ((public.atcis_profile()).country='ALL' or country=(public.atcis_profile()).country)
    and (lower(owner_email)=lower((public.atcis_profile()).email)
      or (public.atcis_profile()).role in ('hod','technical_review','committee','country_admin','super_admin'))
  else public.atcis_country_allowed(country) end
);
commit;
