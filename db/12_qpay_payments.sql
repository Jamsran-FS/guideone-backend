-- ═══════════════════════════════════════════════════════════════════════════
-- QPay V2-оор сургалтын төлбөр төлөх (суралцагч → "Миний бүртгэл" → "QPay-ээр төлөх")
-- 11_notices.sql-ийн дараа нэг удаа ажиллуулна. Дахин ажиллуулж болно.
--
-- Нэхэмжлэх үүсгэх, төлөлт баталгаажуулах нь зөвхөн backend-ээр (Supabase secret key,
-- service_role) явагдана. Суралцагч өөрийн төлбөрийг, sysadmin бүгдийг уншина.
-- ═══════════════════════════════════════════════════════════════════════════
create table if not exists public.payments (
  id                 bigint generated always as identity primary key,
  enrollment_id      bigint not null references public.enrollments (id) on delete cascade,
  student_id         uuid not null references public.profiles (id) on delete cascade,
  amount             numeric(12, 2) not null check (amount > 0),
  status             text not null default 'pending' check (status in ('pending', 'paid', 'cancelled')),
  provider           text not null default 'qpay',
  sender_invoice_no  text not null unique,                 -- манай талын нэхэмжлэхийн дугаар
  invoice_id         text,                                 -- QPay-ийн invoice_id
  invoice            jsonb not null default '{}'::jsonb,   -- qr_text, qr_image, qPay_shortUrl, urls (банкны аппын холбоос)
  callback_token     text not null,                        -- callback URL-ийн нууц — зөвхөн backend уншина
  provider_payment_id text,                                -- QPay-ийн payment_id
  paid_at            timestamptz,
  created_at         timestamptz not null default now(),
  updated_at         timestamptz not null default now()
);

create index if not exists payments_enrollment_idx on public.payments (enrollment_id, created_at desc);
create index if not exists payments_student_idx on public.payments (student_id, created_at desc);

drop trigger if exists payments_updated_at on public.payments;
create trigger payments_updated_at before update on public.payments
  for each row execute function public.set_updated_at();

alter table public.payments enable row level security;
drop policy if exists "payments: read own or admin" on public.payments;
create policy "payments: read own or admin" on public.payments
  for select to authenticated
  using (public.is_admin() or student_id = (select auth.uid()));

-- callback_token-ийг хэрэглэгчид харуулахгүй (багана түвшний эрх)
revoke all on public.payments from anon, authenticated;
grant select (id, enrollment_id, student_id, amount, status, provider, sender_invoice_no, invoice_id, invoice,
  provider_payment_id, paid_at, created_at, updated_at) on public.payments to authenticated;


-- Төлөлт баталгаажсаны дараа нэг гүйлгээнд: payments → paid, enrollments.amount_paid нэмэгдэж,
-- payment_status шинэчлэгдэнэ. Аль хэдийн paid бол юу ч хийхгүй (callback давтагдахад давхар тооцохгүй).
-- Буцаах: энэ дуудалтаар paid болсон эсэх.
create or replace function public.apply_payment(pid bigint, provider_pid text)
returns boolean
language plpgsql
security definer
set search_path = ''
as $$
declare
  p public.payments;
begin
  update public.payments
     set status = 'paid', provider_payment_id = provider_pid, paid_at = now()
   where id = pid and status = 'pending'
  returning * into p;
  if not found then
    return false;
  end if;

  update public.enrollments
     set amount_paid = amount_paid + p.amount,
         payment_status = case
           when amount_paid + p.amount >= amount_due then 'paid'
           else 'partial'
         end
   where id = p.enrollment_id;
  return true;
end;
$$;

revoke execute on function public.apply_payment(bigint, text) from public, anon, authenticated;
grant execute on function public.apply_payment(bigint, text) to service_role;
grant select, insert, update on public.payments to service_role;
