-- ═══════════════════════════════════════════════════════════════════════════
-- Хэрэглэгчийн профайл зураг (avatar)
-- 04_admin.sql-ийн дараа нэг удаа ажиллуулна.
-- Зураг нь Storage "media" bucket-д (нийтэд нээлттэй) хадгалагдаж, энд URL нь бичигдэнэ.
-- ═══════════════════════════════════════════════════════════════════════════
alter table public.profiles
  add column if not exists avatar_url text check (avatar_url is null or char_length(avatar_url) <= 1000);
