-- ═══════════════════════════════════════════════════════════════════════════
-- Хамтрагч сургуулийн дэлгэрэнгүй мэдээлэл (танилцуулга брошюрын мэдээлэл)
-- 07_teacher_education.sql-ийн дараа нэг удаа ажиллуулна.
-- Хүснэгт төрлийн баганууд: [{"label": {...}, "value": {...} | null}] (services.details-тэй ижил)
-- ═══════════════════════════════════════════════════════════════════════════
alter table public.partners
  add column if not exists image_url    text,                                                -- кампусын зураг
  add column if not exists description  jsonb,                                               -- товч танилцуулга
  add column if not exists requirements jsonb not null default '{"mn": [], "en": [], "ko": []}', -- элсэх шаардлага
  add column if not exists tuition      jsonb not null default '[]',                         -- сургалтын төлбөр (салбар → төлбөр)
  add column if not exists scholarships jsonb not null default '[]',                         -- тэтгэлэг (TOPIK 3 → 40%)
  add column if not exists intakes      jsonb not null default '[]',                         -- элсэлтийн хугацаа
  add column if not exists costs        jsonb not null default '[]',                         -- бусад зардал (банкны баталгаа, дотуур байр)
  add column if not exists majors       jsonb not null default '{"mn": [], "en": [], "ko": []}'; -- мэргэжлүүд
