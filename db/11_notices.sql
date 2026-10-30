-- ═══════════════════════════════════════════════════════════════════════════
-- Сайтын дээд талын гүйдэг зар (админ → "Гүйдэг зар")
-- 10_admin_set_password.sql-ийн дараа нэг удаа ажиллуулна. Дахин ажиллуулж болно.
-- starts_at–ends_at хооронд л сайтад харагдана; хугацаа нь дууссан зар админд түүх болж үлдэнэ.
-- ═══════════════════════════════════════════════════════════════════════════
create table if not exists public.notices (
  id            bigint generated always as identity primary key,
  text          jsonb not null,                         -- {"mn": "...", "en": "...", "ko": "..."}
  url           text,                                   -- дарахад очих холбоос (заавал биш)
  starts_at     timestamptz not null default now(),
  ends_at       timestamptz not null,
  sort_order    integer not null default 0,
  is_published  boolean not null default true,
  created_at    timestamptz not null default now(),
  updated_at    timestamptz not null default now(),
  constraint notices_period check (ends_at > starts_at)
);

drop trigger if exists notices_updated_at on public.notices;
create trigger notices_updated_at before update on public.notices
  for each row execute function public.set_updated_at();

alter table public.notices enable row level security;
drop policy if exists "notices: admin manage" on public.notices;
create policy "notices: admin manage" on public.notices
  for all to authenticated using (public.is_admin()) with check (public.is_admin());
drop policy if exists "notices: public read published" on public.notices;
create policy "notices: public read published" on public.notices
  for select to anon, authenticated using (is_published);

grant select on public.notices to anon, authenticated;
grant insert, update, delete on public.notices to authenticated;
