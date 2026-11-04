-- ═══════════════════════════════════════════════════════════════════════════
-- Зургийн цомог (админ → "Зургийн цомог", сайт → /[lang]/gallery)
-- 13_enrollment_course_price.sql-ийн дараа нэг удаа ажиллуулна. Дахин ажиллуулж болно.
-- Зураг нь Storage "media" bucket-д (админы бусад зурагтай адил) хадгалагдана.
-- ═══════════════════════════════════════════════════════════════════════════
create table if not exists public.gallery (
  id            bigint generated always as identity primary key,
  image_url     text not null,
  caption       jsonb,                                  -- {"mn": "...", "en": "...", "ko": "..."} — заавал биш
  width         integer check (width > 0),              -- upload хийхэд бөглөнө (сайтын зургийн харьцаа)
  height        integer check (height > 0),
  sort_order    integer not null default 0,
  is_published  boolean not null default true,
  created_at    timestamptz not null default now(),
  updated_at    timestamptz not null default now()
);

drop trigger if exists gallery_updated_at on public.gallery;
create trigger gallery_updated_at before update on public.gallery
  for each row execute function public.set_updated_at();

alter table public.gallery enable row level security;
drop policy if exists "gallery: admin manage" on public.gallery;
create policy "gallery: admin manage" on public.gallery
  for all to authenticated using (public.is_admin()) with check (public.is_admin());
drop policy if exists "gallery: public read published" on public.gallery;
create policy "gallery: public read published" on public.gallery
  for select to anon, authenticated using (is_published);

grant select on public.gallery to anon, authenticated;
grant insert, update, delete on public.gallery to authenticated;
