-- ═══════════════════════════════════════════════════════════════════════════
-- GuideOne — эрхүүд (sysadmin / teacher / student) ба сургалтын систем
-- 01_schema.sql, 02_seed.sql-ийн ДАРАА нэг удаа ажиллуулна.
--
--   sysadmin — сайтын контент, анги, суралцагч, төлбөр, шалгалтын дүн бүгдийг удирдана
--   teacher  — өөрийн ангийн суралцагчид, материал, зар, шалгалтын дүн
--   student  — нийтийн сайтаар нэвтэрч өөрийн мэдээллээ харна
-- ═══════════════════════════════════════════════════════════════════════════

-- ── 1. Эрхүүд: admin → sysadmin, teacher нэмэх ─────────────────────────────
alter table public.profiles drop constraint if exists profiles_role_check;
update public.profiles set role = 'sysadmin' where role = 'admin';
alter table public.profiles
  add constraint profiles_role_check check (role in ('sysadmin', 'teacher', 'student'));

-- is_admin() нь одоо sysadmin-ийг шалгана (01_schema.sql-ийн policy-ууд үүнийг ашигладаг)
create or replace function public.is_admin()
returns boolean
language sql
stable
security definer
set search_path = ''
as $$
  select exists (
    select 1 from public.profiles
    where id = (select auth.uid()) and role = 'sysadmin'
  );
$$;


-- ── 2. Ангиуд (бүлэг) — нэг багш, олон суралцагч ─────────────────────────
create table public.groups (
  id           bigint generated always as identity primary key,
  name         text not null check (char_length(name) between 1 and 120),
  description  text not null default '',
  teacher_id   uuid references public.profiles (id) on delete set null,
  course_id    bigint references public.courses (id) on delete set null,
  start_date   date,
  end_date     date,
  is_active    boolean not null default true,
  created_at   timestamptz not null default now(),
  updated_at   timestamptz not null default now()
);

create index groups_teacher_idx on public.groups (teacher_id);

-- Суралцагч ангид элссэн байдал + төлбөр
create table public.enrollments (
  id              bigint generated always as identity primary key,
  group_id        bigint not null references public.groups (id) on delete cascade,
  student_id      uuid not null references public.profiles (id) on delete cascade,
  status          text not null default 'active' check (status in ('active', 'completed', 'dropped')),
  payment_status  text not null default 'unpaid' check (payment_status in ('unpaid', 'partial', 'paid')),
  amount_due      numeric(12, 2) not null default 0 check (amount_due >= 0),
  amount_paid     numeric(12, 2) not null default 0 check (amount_paid >= 0),
  note            text not null default '',
  created_at      timestamptz not null default now(),
  updated_at      timestamptz not null default now(),
  unique (group_id, student_id)
);

create index enrollments_student_idx on public.enrollments (student_id);


-- ── 3. Багшийн контент: материал, зар, шалгалт ─────────────────────────────
create table public.materials (
  id           bigint generated always as identity primary key,
  group_id     bigint not null references public.groups (id) on delete cascade,
  author_id    uuid references public.profiles (id) on delete set null default auth.uid(),
  kind         text not null default 'lecture' check (kind in ('handbook', 'lecture', 'seminar', 'other')),
  title        text not null check (char_length(title) between 1 and 200),
  description  text not null default '',
  file_path    text,                     -- Storage "materials" bucket доторх зам: {group_id}/{файл}
  file_name    text,
  link_url     text,                     -- эсвэл гадаад холбоос (YouTube, Google Drive г.м.)
  created_at   timestamptz not null default now()
);

create index materials_group_idx on public.materials (group_id, created_at desc);

create table public.announcements (
  id          bigint generated always as identity primary key,
  group_id    bigint not null references public.groups (id) on delete cascade,
  author_id   uuid references public.profiles (id) on delete set null default auth.uid(),
  title       text not null check (char_length(title) between 1 and 200),
  body        text not null default '' check (char_length(body) <= 8000),
  created_at  timestamptz not null default now()
);

create index announcements_group_idx on public.announcements (group_id, created_at desc);

create table public.exams (
  id           bigint generated always as identity primary key,
  group_id     bigint not null references public.groups (id) on delete cascade,
  title        text not null check (char_length(title) between 1 and 200),
  description  text not null default '',
  exam_date    date,
  max_score    numeric(6, 2) not null default 100 check (max_score > 0),
  created_by   uuid references public.profiles (id) on delete set null default auth.uid(),
  created_at   timestamptz not null default now()
);

create index exams_group_idx on public.exams (group_id, exam_date);

create table public.exam_results (
  id          bigint generated always as identity primary key,
  exam_id     bigint not null references public.exams (id) on delete cascade,
  student_id  uuid not null references public.profiles (id) on delete cascade,
  score       numeric(6, 2) check (score >= 0),
  comment     text not null default '',
  updated_at  timestamptz not null default now(),
  unique (exam_id, student_id)
);

create index exam_results_student_idx on public.exam_results (student_id);

create trigger groups_updated_at before update on public.groups
  for each row execute function public.set_updated_at();
create trigger enrollments_updated_at before update on public.enrollments
  for each row execute function public.set_updated_at();
create trigger exam_results_updated_at before update on public.exam_results
  for each row execute function public.set_updated_at();


-- ── 4. RLS туслах функцууд (security definer — рекурсээс сэргийлнэ) ────────
create or replace function public.teaches_group(gid bigint)
returns boolean language sql stable security definer set search_path = '' as $$
  select exists (select 1 from public.groups where id = gid and teacher_id = (select auth.uid()));
$$;

-- Суралцагч тухайн ангид (идэвхтэй эсвэл төгссөн) байгаа эсэх
create or replace function public.in_group(gid bigint)
returns boolean language sql stable security definer set search_path = '' as $$
  select exists (
    select 1 from public.enrollments
    where group_id = gid and student_id = (select auth.uid()) and status in ('active', 'completed')
  );
$$;

-- Багш энэ суралцагчид хичээл заадаг эсэх
create or replace function public.teaches_student(sid uuid)
returns boolean language sql stable security definer set search_path = '' as $$
  select exists (
    select 1 from public.enrollments e join public.groups g on g.id = e.group_id
    where e.student_id = sid and g.teacher_id = (select auth.uid())
  );
$$;

-- Суралцагчийн багш мөн эсэх (суралцагч багшийнхаа нэрийг харна)
create or replace function public.is_my_teacher(tid uuid)
returns boolean language sql stable security definer set search_path = '' as $$
  select exists (
    select 1 from public.enrollments e join public.groups g on g.id = e.group_id
    where g.teacher_id = tid and e.student_id = (select auth.uid())
  );
$$;

create or replace function public.exam_group(eid bigint)
returns bigint language sql stable security definer set search_path = '' as $$
  select group_id from public.exams where id = eid;
$$;

grant execute on function public.teaches_group(bigint), public.in_group(bigint), public.teaches_student(uuid),
  public.is_my_teacher(uuid), public.exam_group(bigint) to authenticated;


-- ── 5. RLS ──────────────────────────────────────────────────────────────────
-- profiles: өөрийгөө, sysadmin бүгдийг, багш өөрийн суралцагчдыг, суралцагч өөрийн багшийг харна
drop policy if exists "profiles: read own or admin" on public.profiles;
create policy "profiles: read" on public.profiles
  for select to authenticated
  using (
    id = (select auth.uid()) or public.is_admin()
    or public.teaches_student(id) or public.is_my_teacher(id)
  );

alter table public.groups enable row level security;
create policy "groups: read" on public.groups
  for select to authenticated
  using (public.is_admin() or teacher_id = (select auth.uid()) or public.in_group(id));
create policy "groups: sysadmin manage" on public.groups
  for all to authenticated using (public.is_admin()) with check (public.is_admin());

alter table public.enrollments enable row level security;
create policy "enrollments: read" on public.enrollments
  for select to authenticated
  using (public.is_admin() or student_id = (select auth.uid()) or public.teaches_group(group_id));
create policy "enrollments: sysadmin manage" on public.enrollments
  for all to authenticated using (public.is_admin()) with check (public.is_admin());

-- materials, announcements, exams: ангийн суралцагчид уншина, ангийн багш ба sysadmin удирдана
do $$
declare
  t text;
begin
  foreach t in array array['materials', 'announcements', 'exams'] loop
    execute format('alter table public.%I enable row level security', t);
    execute format(
      'create policy %I on public.%I for select to authenticated using (public.is_admin() or public.teaches_group(group_id) or public.in_group(group_id))',
      t || ': read', t);
    execute format(
      'create policy %I on public.%I for all to authenticated using (public.is_admin() or public.teaches_group(group_id)) with check (public.is_admin() or public.teaches_group(group_id))',
      t || ': teacher manage', t);
  end loop;
end;
$$;

alter table public.exam_results enable row level security;
create policy "exam_results: read" on public.exam_results
  for select to authenticated
  using (
    public.is_admin() or student_id = (select auth.uid())
    or public.teaches_group(public.exam_group(exam_id))
  );
create policy "exam_results: teacher manage" on public.exam_results
  for all to authenticated
  using (public.is_admin() or public.teaches_group(public.exam_group(exam_id)))
  with check (public.is_admin() or public.teaches_group(public.exam_group(exam_id)));


-- ── 6. Сургалтын материалын файл — ХААЛТТАЙ bucket ──────────────────────────
-- Зам: {group_id}/{файл}. Зөвхөн тухайн ангийн суралцагч, багш, sysadmin татна (signed URL).
insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values (
  'materials', 'materials', false, 20971520,
  array[
    'application/pdf',
    'application/msword', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
    'application/vnd.ms-powerpoint', 'application/vnd.openxmlformats-officedocument.presentationml.presentation',
    'application/vnd.ms-excel', 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    'image/jpeg', 'image/png', 'image/webp',
    'audio/mpeg', 'audio/mp4', 'audio/x-m4a',
    'application/zip'
  ]
)
on conflict (id) do nothing;

create policy "materials files: read" on storage.objects
  for select to authenticated
  using (
    bucket_id = 'materials' and (
      public.is_admin()
      or public.teaches_group(((storage.foldername(name))[1])::bigint)
      or public.in_group(((storage.foldername(name))[1])::bigint)
    )
  );

create policy "materials files: teacher upload" on storage.objects
  for insert to authenticated
  with check (
    bucket_id = 'materials' and (
      public.is_admin() or public.teaches_group(((storage.foldername(name))[1])::bigint)
    )
  );

create policy "materials files: teacher delete" on storage.objects
  for delete to authenticated
  using (
    bucket_id = 'materials' and (
      public.is_admin() or public.teaches_group(((storage.foldername(name))[1])::bigint)
    )
  );


-- ── 7. Эрхүүд (Data API) ────────────────────────────────────────────────────
grant select, insert, update, delete on public.groups, public.enrollments, public.materials,
  public.announcements, public.exams, public.exam_results to authenticated;
