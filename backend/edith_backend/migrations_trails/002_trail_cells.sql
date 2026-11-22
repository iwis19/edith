-- Grid cells walked per profile and day, kept up to date by Timescale. A cell is 0.002 degrees square, about 220 m of
-- latitude; trails.CELL_DEG must stay equal to it. Without Timescale the same cells are read from trail_points directly.
do $$
begin
    if exists (select 1 from pg_extension where extname = 'timescaledb') then
        execute $view$
            create materialized view trail_cells_daily
            with (timescaledb.continuous, timescaledb.materialized_only = false) as
            select profile_id,
                   time_bucket(interval '1 day', taken_at) as day,
                   floor(lat / 0.002)::int as cell_lat,
                   floor(lon / 0.002)::int as cell_lon,
                   count(*) as points
            from trail_points
            group by profile_id, time_bucket(interval '1 day', taken_at), floor(lat / 0.002)::int, floor(lon / 0.002)::int
            with no data
        $view$;
        perform add_continuous_aggregate_policy('trail_cells_daily',
            start_offset => interval '3 days', end_offset => interval '1 hour', schedule_interval => interval '1 hour');
    end if;
end
$$;
