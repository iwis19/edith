create table hunts (
    id uuid primary key,
    profile_id uuid not null references profiles (id) on delete cascade,
    started_at timestamptz not null,
    ends_at timestamptz not null,
    ended_at timestamptz,
    end_reason text check (end_reason in ('found_all', 'stopped')),
    place text
);

create index hunts_profile_started_idx on hunts (profile_id, started_at desc);
create index hunts_active_idx on hunts (ends_at) where ended_at is null;

create table hunt_targets (
    hunt_id uuid not null references hunts (id) on delete cascade,
    idx smallint not null,
    text text not null,
    tier text check (tier in ('common', 'uncommon', 'rare', 'epic', 'legendary', 'mythic')),
    found_at timestamptz,
    -- No foreign key: the Look row is written after its stream ends, which can be after the tick.
    look_ref uuid,
    primary key (hunt_id, idx)
);

create index hunt_targets_look_ref_idx on hunt_targets (look_ref) where look_ref is not null;
