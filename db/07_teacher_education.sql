-- ═══════════════════════════════════════════════════════════════════════════
-- Багш нарын боловсрол (төгссөн сургууль, мэргэжил, зэрэг, он, төлөв)
-- 06_staff_autoconfirm.sql-ийн дараа нэг удаа ажиллуулна.
-- [{"school": {...}, "field": {...} | null, "degree": "bachelor" | "master" | "phd" | "training" | null,
--   "start": "2014" | null, "end": "2018" | null, "status": "completed" | "in_progress"}]
-- ═══════════════════════════════════════════════════════════════════════════
alter table public.teachers
  add column if not exists education jsonb not null default '[]';
