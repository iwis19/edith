create extension if not exists vector;

create table profiles (
    id uuid primary key,
    created_at timestamptz not null default now()
);

create table looks (
    id uuid primary key,
    profile_id uuid not null references profiles (id) on delete cascade,
    client_look_id integer not null,
    source text not null check (source in ('pinch', 'scan')),
    captured_at timestamptz not null,
    width integer not null,
    height integer not null,
    status text not null check (status in ('done', 'error')),
    name text,
    confidence real,
    fact text,
    fact_sources jsonb not null default '[]',
    rarity text check (rarity in ('common', 'uncommon', 'rare', 'epic', 'legendary', 'mythic')),
    duration_ms integer not null,
    created_at timestamptz not null default now()
);

create index looks_profile_created_idx on looks (profile_id, created_at desc);
