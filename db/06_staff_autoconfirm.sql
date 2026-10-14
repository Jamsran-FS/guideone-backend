-- ═══════════════════════════════════════════════════════════════════════════
-- Sysadmin, багшийн и-мэйлийг автоматаар баталгаажуулах
-- И-мэйл баталгаажуулалт зөвхөн сайтаар бүртгүүлсэн суралцагчдад үйлчилнэ.
-- 05_avatar.sql-ийн дараа нэг удаа ажиллуулна.
-- ═══════════════════════════════════════════════════════════════════════════

-- Profile-ийн эрх sysadmin/teacher болох (эсвэл тийм эрхтэй үүсэх) бүрд auth.users-д баталгаажсан гэж тэмдэглэнэ
create or replace function public.confirm_staff_email()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
  if new.role in ('sysadmin', 'teacher') then
    update auth.users
    set email_confirmed_at = now()
    where id = new.id and email_confirmed_at is null;
  end if;
  return new;
end;
$$;

drop trigger if exists profiles_confirm_staff on public.profiles;
create trigger profiles_confirm_staff
  after insert or update of role on public.profiles
  for each row execute function public.confirm_staff_email();

-- Одоо байгаа, баталгаажаагүй sysadmin/багш нарыг баталгаажуулна
update auth.users u
set email_confirmed_at = now()
from public.profiles p
where p.id = u.id
  and p.role in ('sysadmin', 'teacher')
  and u.email_confirmed_at is null;

-- Шалгах
select p.email, p.role, u.email_confirmed_at
from public.profiles p
join auth.users u on u.id = p.id
order by p.role, p.email;
