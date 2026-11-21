alter table looks add column kind text;
alter table looks add column place text;

create table memories (
    id bigint generated always as identity primary key,
    profile_id uuid not null references profiles (id) on delete cascade,
    look_ref uuid not null unique references looks (id) on delete cascade,
    name text not null,
    kind text,
    fact text,
    sources jsonb not null default '[]',
    rarity text check (rarity in ('common', 'uncommon', 'rare', 'epic', 'legendary', 'mythic')),
    place text,
    image bytea not null,
    -- Must equal Settings.embedding_dimensions; test_postgres checks it.
    embedding vector(1536),
    created_at timestamptz not null default now()
);

create index memories_profile_created_idx on memories (profile_id, created_at desc, id desc);
