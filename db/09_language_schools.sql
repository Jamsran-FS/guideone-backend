-- ═══════════════════════════════════════════════════════════════════════════
-- Хэлний бэлтгэлийн сургуулиуд (сайтын "Хэлний бэлтгэл" цэс)
-- 08_partner_details.sql-ийн дараа нэг удаа ажиллуулна.
-- scholarships: [{"label": {...}, "value": {...} | null}] (partners.scholarships-тэй ижил)
-- ═══════════════════════════════════════════════════════════════════════════
create table if not exists public.language_schools (
  id            bigint generated always as identity primary key,
  name          jsonb not null,
  city          jsonb,                                                         -- байршил: Инчон хот
  visa_type     text check (visa_type is null or visa_type in ('e_visa', 'regular')), -- цахим / цахим бус виз
  tuition       jsonb,                                                         -- сургалтын төлбөр: 1,000,000₩
  headline      jsonb,                                                         -- онцлох мөр: Хэлний бэлтгэл + Бакалавр = 4 жил
  highlights    jsonb not null default '{"mn": [], "en": [], "ko": []}',       -- давуу талууд (мөр бүрд нэг)
  scholarships  jsonb not null default '[]',                                   -- TOPIK 3 → 40%
  description   jsonb,
  logo_url      text,
  image_url     text,
  url           text,
  sort_order    integer not null default 0,
  is_published  boolean not null default true,
  created_at    timestamptz not null default now(),
  updated_at    timestamptz not null default now()
);

-- 01_schema.sql-ийн бусад контент хүснэгттэй ижил trigger, RLS, эрх
create trigger language_schools_updated_at before update on public.language_schools
  for each row execute function public.set_updated_at();
alter table public.language_schools enable row level security;
create policy "language_schools: admin manage" on public.language_schools
  for all to authenticated using (public.is_admin()) with check (public.is_admin());
create policy "language_schools: public read published" on public.language_schools
  for select to anon, authenticated using (is_published);

grant select on public.language_schools to anon, authenticated;
grant insert, update, delete on public.language_schools to authenticated;
