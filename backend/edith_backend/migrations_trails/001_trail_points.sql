-- Fixes recorded while someone is navigating. No foreign keys: this database can be separate from the one holding
-- profiles. Fixes are rounded to 4 decimals before they are stored.
create table trail_points (
    profile_id uuid not null,
    adventure_id uuid not null,
    taken_at timestamptz not null,
    lat double precision not null,
    lon double precision not null,
    accuracy_m real not null
);

-- A hypertable's unique indexes must include the time column.
create unique index trail_points_adventure_time_idx on trail_points (adventure_id, taken_at);
create index trail_points_profile_time_idx on trail_points (profile_id, taken_at desc);

-- Plain Postgres keeps an ordinary table; Timescale partitions it by time.
do $$
begin
    if exists (select 1 from pg_extension where extname = 'timescaledb')
       or exists (select 1 from pg_available_extensions where name = 'timescaledb') then
        create extension if not exists timescaledb;
        perform create_hypertable('trail_points', 'taken_at', migrate_data => true);
    end if;
end
$$;
