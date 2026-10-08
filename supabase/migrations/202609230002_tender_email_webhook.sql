begin;

create extension if not exists pg_net with schema extensions;

create or replace function public.notify_new_tender_email()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
  function_key text;
begin
  select decrypted_secret
    into function_key
    from vault.decrypted_secrets
   where name = 'atcis_tender_webhook_secret'
   limit 1;

  if function_key is null then
    raise warning 'Tender alert webhook key is not configured';
    return new;
  end if;

  perform net.http_post(
    url := 'https://pqqymbdbkwltzydymild.supabase.co/functions/v1/send-tender-alert',
    headers := jsonb_build_object(
      'Content-Type', 'application/json',
      'x-atcis-webhook-secret', function_key
    ),
    body := jsonb_build_object(
      'type', tg_op,
      'table', tg_table_name,
      'schema', tg_table_schema,
      'record', to_jsonb(new),
      'old_record', null
    ),
    timeout_milliseconds := 5000
  );

  return new;
end;
$$;

revoke all on function public.notify_new_tender_email() from public, anon, authenticated;

drop trigger if exists tender_insert_email_webhook on public.tenders;
create trigger tender_insert_email_webhook
after insert on public.tenders
for each row execute function public.notify_new_tender_email();

commit;
