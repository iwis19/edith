-- The facts each profile was told, with the embedding of the kind of object each was about, so a later Look of the
-- same kind under another name ("mug", "coffee cup") finds them after a restart. memories.embedding is left as it is:
-- nothing writes it.
create table told_facts (
    id bigint generated always as identity primary key,
    profile_id uuid not null references profiles (id) on delete cascade,
    kind text not null,
    -- Must equal Settings.embedding_dimensions; test_postgres checks it. Null when the kind could not be embedded.
    kind_embedding vector(1536),
    fact text not null,
    told_at timestamptz not null default now(),
    unique (profile_id, fact)
);

create index told_facts_profile_told_idx on told_facts (profile_id, told_at desc);
