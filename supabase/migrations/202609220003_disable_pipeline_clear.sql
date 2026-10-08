begin;

-- Whole-board deletion was replaced with a server-authorized single-tender
-- action. Remove the legacy RPC so no client can clear an entire pipeline.
drop function if exists public.admin_clear_pipeline_records(text);

commit;
