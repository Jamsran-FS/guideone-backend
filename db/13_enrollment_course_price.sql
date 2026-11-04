-- ═══════════════════════════════════════════════════════════════════════════
-- Ангид элсүүлэхэд төлөх дүнг (enrollments.amount_due) ангийн курсийн "Төлбөр"-өөс бөглөнө
-- 12_qpay_payments.sql-ийн дараа нэг удаа ажиллуулна. Дахин ажиллуулж болно.
--
-- courses.price нь сайтад харуулах текст ({"mn": "1,200,000₮", ...}). Зөвхөн цэвэр тоо
-- (зай, таслал, ₮, "төгрөг"-өөс бусад тэмдэгтгүй) бол дүн болгоно — "1.2 сая" гэх мэт бол 0 хэвээр,
-- админ гараар оруулна. Админ элсүүлэхдээ дүн өгсөн (0-ээс их) бол түүнийг хэвээр үлдээнэ.
-- ═══════════════════════════════════════════════════════════════════════════
create or replace function public.course_price_amount(price jsonb)
returns numeric
language sql
immutable
set search_path = ''
as $$
  select case when cleaned ~ '^[0-9]+$' then cleaned::numeric end
  from (
    select regexp_replace(lower(coalesce(price ->> 'mn', '')), '[[:space:],''₮]|төгрөг|төг', '', 'g') as cleaned
  ) t;
$$;

create or replace function public.enrollments_default_amount()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
  if new.amount_due = 0 then
    select coalesce(public.course_price_amount(c.price), 0) into new.amount_due
    from public.groups g join public.courses c on c.id = g.course_id
    where g.id = new.group_id;
    new.amount_due := coalesce(new.amount_due, 0);
  end if;
  return new;
end;
$$;

drop trigger if exists enrollments_default_amount on public.enrollments;
create trigger enrollments_default_amount before insert on public.enrollments
  for each row execute function public.enrollments_default_amount();

-- Өмнө нь 0₮-тэй элссэн, огт төлөөгүй элсэлтүүдийг нөхөж бөглөнө
update public.enrollments e
   set amount_due = public.course_price_amount(c.price)
  from public.groups g join public.courses c on c.id = g.course_id
 where g.id = e.group_id
   and e.amount_due = 0 and e.amount_paid = 0
   and public.course_price_amount(c.price) > 0;
