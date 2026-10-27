-- Deletes the sign-up bots of oct 2026: accounts in no band whose display
-- name is a random run of 14+ letters. Run inside the postgres container:
--   docker compose -f docker-compose.prod.yml exec -T postgres psql -U bandroom -d bandroom -v ON_ERROR_STOP=1 < ops/delete-bot-accounts.sql
-- Take a backup first (bash deploy/backup.sh). Real accounts are excluded twice:
-- by having a band, and by the explicit email list.
\echo --- before
select count(*) as users_total,
       count(*) filter (where not exists (select 1 from "Memberships" m where m."UserId" = u."Id")) as bandless,
       count(*) filter (where not exists (select 1 from "Memberships" m where m."UserId" = u."Id")
                          and u."DisplayName" ~ '^[A-Za-z]{14,}$') as bot_shaped
from "AspNetUsers" u;

begin;
create temp table bots as
  select u."Id"
  from "AspNetUsers" u
  where not exists (select 1 from "Memberships" m where m."UserId" = u."Id")
    and u."DisplayName" ~ '^[A-Za-z]{14,}$'
    and u."Email" not in ('zamiams@gmail.com', 'lasse@tinygiants.dk', 'smoketest+prod@bandroom.io');
delete from "RefreshTokens"     where "UserId" in (select "Id" from bots);
delete from "PushSubscriptions" where "UserId" in (select "Id" from bots);
delete from "AspNetUserTokens"  where "UserId" in (select "Id" from bots);
delete from "AspNetUsers"       where "Id"     in (select "Id" from bots);
\echo --- deleted
select count(*) as deleted from bots;
commit;

\echo --- remaining accounts
select "Email", "DisplayName",
       (select count(*) from "Memberships" m where m."UserId" = u."Id") as bands
from "AspNetUsers" u order by "Email";
