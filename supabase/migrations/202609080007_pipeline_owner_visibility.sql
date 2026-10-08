begin;

-- Pipeline business figures are private to the owner. Country admins and HODs
-- have country oversight; technical/committee access to review packages remains
-- governed by the existing review policies.
drop policy if exists pipeline_owner_visibility on public.app_records;
create policy pipeline_owner_visibility on public.app_records
  as restrictive for select to authenticated
  using (
    kind <> 'pipeline'
    or exists (
      select 1 from public.profiles p
      where p.id = auth.uid() and p.active
        and (
          p.role = 'super_admin'
          or (
            app_records.country = p.country
            and (
              lower(app_records.owner_email) = lower(p.email)
              or p.role in ('hod', 'country_admin')
            )
          )
        )
    )
  );

notify pgrst, 'reload schema';
commit;
