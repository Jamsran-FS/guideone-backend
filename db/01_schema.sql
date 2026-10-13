-- ═══════════════════════════════════════════════════════════════════════════
-- GuideOne — Supabase өгөгдлийн сангийн бүтэц
-- Supabase Dashboard → SQL Editor дээр бүтнээр нь нэг удаа ажиллуулна.
-- Дараа нь 02_seed.sql (одоогийн сайтын контент), эцэст нь 03_admin.sql.
--
-- Хэл бүрээр хадгалах текст (Localized) нь jsonb: {"mn": "...", "en": "...", "ko": "..."}
-- ═══════════════════════════════════════════════════════════════════════════

-- ── Туслах функц: updated_at-ийг автоматаар шинэчилнэ ──────────────────────
create or replace function public.set_updated_at()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
  new.updated_at := now();
  return new;
end;
$$;


-- ═══════════════════════════════════════════════════════════════════════════
-- 1. ХЭРЭГЛЭГЧИД — profiles (auth.users-тэй 1:1)
--    role: 'admin' — сайтын контент, хүсэлт, өргөдөл удирдана
--          'student' — суралцагч (бүртгүүлэхэд автоматаар)
-- ═══════════════════════════════════════════════════════════════════════════
create table public.profiles (
  id            uuid primary key references auth.users (id) on delete cascade,
  role          text not null default 'student' check (role in ('admin', 'student')),
  email         text,
  full_name     text not null default '' check (char_length(full_name) <= 120),
  phone         text not null default '' check (char_length(phone) <= 40),
  birth_date    date,
  parent_name   text not null default '' check (char_length(parent_name) <= 120),
  parent_phone  text not null default '' check (char_length(parent_phone) <= 40),
  korean_level  text not null default '' check (char_length(korean_level) <= 60),
  created_at    timestamptz not null default now(),
  updated_at    timestamptz not null default now()
);

-- RLS policy-д ашиглана. security definer — profiles-ийн RLS-ийг тойрч рекурс үүсэхээс сэргийлнэ.
create or replace function public.is_admin()
returns boolean
language sql
stable
security definer
set search_path = ''
as $$
  select exists (
    select 1 from public.profiles
    where id = (select auth.uid()) and role = 'admin'
  );
$$;

-- Шинэ хэрэглэгч бүртгүүлэхэд profile үүсгэнэ. role-ийг metadata-аас ХЭЗЭЭ Ч авахгүй — үргэлж student.
create or replace function public.handle_new_user()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
  insert into public.profiles (id, email, full_name, phone)
  values (
    new.id,
    new.email,
    left(coalesce(new.raw_user_meta_data ->> 'full_name', ''), 120),
    left(coalesce(new.raw_user_meta_data ->> 'phone', ''), 40)
  );
  return new;
end;
$$;

create trigger on_auth_user_created
  after insert on auth.users
  for each row execute function public.handle_new_user();

-- role-ийг зөвхөн админ солино; email-ийг auth.users-ээс өөрчлөхгүй.
-- (SQL Editor-оос буюу JWT-гүй үед хязгаарлахгүй — анхны админыг ингэж томилно.)
create or replace function public.protect_profile()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
  if (select auth.uid()) is not null then
    if new.role is distinct from old.role and not public.is_admin() then
      raise exception 'Only admins can change roles' using errcode = '42501';
    end if;
    new.email := old.email;
  end if;
  return new;
end;
$$;

create trigger profiles_protect before update on public.profiles
  for each row execute function public.protect_profile();
create trigger profiles_updated_at before update on public.profiles
  for each row execute function public.set_updated_at();

alter table public.profiles enable row level security;

create policy "profiles: read own or admin" on public.profiles
  for select to authenticated
  using (id = (select auth.uid()) or public.is_admin());

create policy "profiles: update own or admin" on public.profiles
  for update to authenticated
  using (id = (select auth.uid()) or public.is_admin())
  with check (id = (select auth.uid()) or public.is_admin());


-- ═══════════════════════════════════════════════════════════════════════════
-- 2. САЙТЫН КОНТЕНТ — админ засна, бүх хүн уншина
-- ═══════════════════════════════════════════════════════════════════════════

-- Хуудасны текстүүд: key = 'hero', 'intro', 'site' (хаяг, утас) г.м., value = тухайн хэсгийн JSON
create table public.site_content (
  key         text primary key check (key ~ '^[a-zA-Z][a-zA-Z0-9_-]{0,63}$'),
  value       jsonb not null,
  updated_at  timestamptz not null default now()
);

-- Солонгос хэлний сургалтууд (хэлний бэлтгэл)
create table public.courses (
  id              bigint generated always as identity primary key,
  slug            text not null unique check (slug ~ '^[a-z0-9-]+$'),
  glyph           text not null default '',          -- карт дээрх том солонгос тэмдэгт, жишээ нь 초급
  name            jsonb not null,
  level           jsonb not null,
  description     jsonb not null,
  duration        jsonb,                             -- null → "Зөвлөхөөс тодруулна"
  price           jsonb,
  is_placeholder  boolean not null default false,
  sort_order      integer not null default 0,
  is_published    boolean not null default true,
  created_at      timestamptz not null default now(),
  updated_at      timestamptz not null default now()
);

-- Үйлчилгээ: status 'active' → /services/[slug] хуудастай, 'upcoming' → "Тун удахгүй"
create table public.services (
  id            bigint generated always as identity primary key,
  slug          text not null unique check (slug ~ '^[a-z0-9-]+$'),
  icon          text not null default 'GraduationCap',
  status        text not null default 'active' check (status in ('active', 'upcoming')),
  title         jsonb not null,
  summary       jsonb,                               -- нүүр хуудасны картын богино тайлбар
  description   jsonb not null,
  topics        jsonb not null default '{"mn": [], "en": [], "ko": []}',   -- {"mn": ["..."], ...}
  details       jsonb not null default '[]',         -- [{"label": {...}, "value": {...} | null}]
  is_new        boolean not null default false,
  sort_order    integer not null default 0,
  is_published  boolean not null default true,
  created_at    timestamptz not null default now(),
  updated_at    timestamptz not null default now()
);

-- Солонгос дахь хамтрагч сургуулиуд (зуучлал)
create table public.partners (
  id            bigint generated always as identity primary key,
  name          jsonb not null,
  city          jsonb,
  type          jsonb,                               -- Их сургууль, Хэлний сургууль г.м.
  logo_url      text,
  url           text,
  sort_order    integer not null default 0,
  is_published  boolean not null default true,
  created_at    timestamptz not null default now(),
  updated_at    timestamptz not null default now()
);

create table public.teachers (
  id            bigint generated always as identity primary key,
  name          text not null,
  image_url     text,
  position      jsonb not null,
  specialty     jsonb,
  experience    jsonb,
  description   jsonb,
  sort_order    integer not null default 0,
  is_published  boolean not null default true,
  created_at    timestamptz not null default now(),
  updated_at    timestamptz not null default now()
);

-- Суралцагчдын сэтгэгдэл — зөвхөн бодит, зөвшөөрөл авсан сэтгэгдэл
create table public.testimonials (
  id            bigint generated always as identity primary key,
  name          text not null,
  context       jsonb,                               -- жишээ нь "TOPIK II бэлтгэл"
  quote         jsonb not null,
  image_url     text,
  sort_order    integer not null default 0,
  is_published  boolean not null default true,
  created_at    timestamptz not null default now(),
  updated_at    timestamptz not null default now()
);

create table public.faqs (
  id            bigint generated always as identity primary key,
  question      jsonb not null,
  answer        jsonb not null,
  sort_order    integer not null default 0,
  is_published  boolean not null default true,
  created_at    timestamptz not null default now(),
  updated_at    timestamptz not null default now()
);

-- updated_at trigger + RLS (бүгд уншина — нийтлэгдсэнийг; админ бүгдийг удирдана)
do $$
declare
  t text;
begin
  foreach t in array array['site_content', 'courses', 'services', 'partners', 'teachers', 'testimonials', 'faqs'] loop
    execute format('create trigger %I before update on public.%I for each row execute function public.set_updated_at()', t || '_updated_at', t);
    execute format('alter table public.%I enable row level security', t);
    execute format('create policy %I on public.%I for all to authenticated using (public.is_admin()) with check (public.is_admin())', t || ': admin manage', t);
  end loop;

  foreach t in array array['courses', 'services', 'partners', 'teachers', 'testimonials', 'faqs'] loop
    execute format('create policy %I on public.%I for select to anon, authenticated using (is_published)', t || ': public read published', t);
  end loop;
end;
$$;

create policy "site_content: public read" on public.site_content
  for select to anon, authenticated using (true);


-- ═══════════════════════════════════════════════════════════════════════════
-- 3. ХОЛБОО БАРИХ ХҮСЭЛТ — сайтын формоос (нэвтрээгүй хүн ч илгээнэ)
-- ═══════════════════════════════════════════════════════════════════════════
create table public.contact_requests (
  id          bigint generated always as identity primary key,
  name        text not null check (char_length(name) between 1 and 120),
  phone       text not null check (char_length(phone) between 6 and 40),
  interest    text not null default '' check (char_length(interest) <= 40),
  message     text not null default '' check (char_length(message) <= 4000),
  locale      text not null default 'mn' check (locale in ('mn', 'en', 'ko')),
  status      text not null default 'new' check (status in ('new', 'contacted', 'closed')),
  admin_note  text not null default '',
  created_at  timestamptz not null default now(),
  updated_at  timestamptz not null default now()
);

create index contact_requests_status_idx on public.contact_requests (status, created_at desc);
create trigger contact_requests_updated_at before update on public.contact_requests
  for each row execute function public.set_updated_at();

alter table public.contact_requests enable row level security;

create policy "contact_requests: anyone can submit" on public.contact_requests
  for insert to anon, authenticated
  with check (status = 'new' and admin_note = '');

create policy "contact_requests: admin manage" on public.contact_requests
  for all to authenticated
  using (public.is_admin()) with check (public.is_admin());


-- ═══════════════════════════════════════════════════════════════════════════
-- 4. ӨРГӨДӨЛ — суралцагч хэлний бэлтгэл эсвэл Солонгос сургуульд зуучлал хүснэ
-- ═══════════════════════════════════════════════════════════════════════════
create table public.applications (
  id          bigint generated always as identity primary key,
  student_id  uuid not null default auth.uid() references public.profiles (id) on delete cascade,
  program     text not null check (program in ('language_course', 'school_placement')),
  course_id   bigint references public.courses (id) on delete set null,
  partner_id  bigint references public.partners (id) on delete set null,
  message     text not null default '' check (char_length(message) <= 4000),
  status      text not null default 'new' check (status in ('new', 'in_review', 'accepted', 'rejected', 'cancelled')),
  admin_note  text not null default '',
  created_at  timestamptz not null default now(),
  updated_at  timestamptz not null default now()
);

create index applications_student_idx on public.applications (student_id, created_at desc);
create index applications_status_idx on public.applications (status, created_at desc);
create trigger applications_updated_at before update on public.applications
  for each row execute function public.set_updated_at();

alter table public.applications enable row level security;

create policy "applications: read own or admin" on public.applications
  for select to authenticated
  using (student_id = (select auth.uid()) or public.is_admin());

-- Суралцагч зөвхөн өөрийн нэрээр, "new" төлөвтэй өргөдөл үүсгэнэ. Төлөвийг зөвхөн админ солино.
create policy "applications: student submit own" on public.applications
  for insert to authenticated
  with check (student_id = (select auth.uid()) and status = 'new' and admin_note = '');

create policy "applications: admin update" on public.applications
  for update to authenticated
  using (public.is_admin()) with check (public.is_admin());

create policy "applications: admin delete" on public.applications
  for delete to authenticated
  using (public.is_admin());


-- ═══════════════════════════════════════════════════════════════════════════
-- 5. ЗУРАГ — Storage "media" bucket (багш, сургуулийн лого, hero зураг)
--    Нийтэд нээлттэй уншина, зөвхөн админ upload/устгана.
-- ═══════════════════════════════════════════════════════════════════════════
insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values ('media', 'media', true, 5242880, array['image/jpeg', 'image/png', 'image/webp', 'image/gif'])
on conflict (id) do nothing;

create policy "media: admin upload" on storage.objects
  for insert to authenticated
  with check (bucket_id = 'media' and public.is_admin());

create policy "media: admin update" on storage.objects
  for update to authenticated
  using (bucket_id = 'media' and public.is_admin());

create policy "media: admin delete" on storage.objects
  for delete to authenticated
  using (bucket_id = 'media' and public.is_admin());


-- ═══════════════════════════════════════════════════════════════════════════
-- 6. ЭРХҮҮД (Data API) — RLS дээрх policy-уудтай хамт ажиллана
-- ═══════════════════════════════════════════════════════════════════════════
grant select on public.site_content, public.courses, public.services, public.partners,
                 public.teachers, public.testimonials, public.faqs to anon, authenticated;
grant insert, update, delete on public.site_content, public.courses, public.services, public.partners,
                 public.teachers, public.testimonials, public.faqs to authenticated;
grant insert on public.contact_requests to anon, authenticated;
grant select, update, delete on public.contact_requests to authenticated;
grant select, update on public.profiles to authenticated;
grant select, insert, update, delete on public.applications to authenticated;
grant execute on function public.is_admin() to anon, authenticated;
