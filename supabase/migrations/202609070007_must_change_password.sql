-- Add must_change_password flag to profiles table for first-time login enforcement
alter table public.profiles add column if not exists must_change_password boolean not null default false;
