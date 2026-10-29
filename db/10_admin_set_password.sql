-- ═══════════════════════════════════════════════════════════════════════════
-- Sysadmin хэрэглэгчийн нууц үгийг солих (админ → Хэрэглэгч, эрх → Нууц үг)
-- 09_language_schools.sql-ийн дараа нэг удаа ажиллуулна. Дахин ажиллуулж болно.
-- Supabase:SecretKey шаардахгүй — зөвхөн is_admin() хэрэглэгч дуудна.
-- ═══════════════════════════════════════════════════════════════════════════
create or replace function public.admin_set_password(target uuid, new_password text)
returns void
language plpgsql
security definer
set search_path = ''
as $$
begin
  if not public.is_admin() then
    raise exception 'Only sysadmins can change passwords.' using errcode = '42501';
  end if;
  if length(coalesce(new_password, '')) < 8 or length(new_password) > 72 then
    raise exception 'Password must be 8-72 characters.' using errcode = '22023';
  end if;

  -- Supabase Auth bcrypt hash ашигладаг
  update auth.users
  set encrypted_password = extensions.crypt(new_password, extensions.gen_salt('bf')),
      updated_at = now()
  where id = target;

  if not found then
    raise exception 'User not found.' using errcode = 'P0002';
  end if;
end;
$$;

revoke all on function public.admin_set_password(uuid, text) from public, anon;
grant execute on function public.admin_set_password(uuid, text) to authenticated;
