-- The place the photo itself showed, which the card agent loads at startup as the person's recent places. place holds
-- the phone's place when there is one, and a place the model copied from its own hint is never kept here.
alter table looks add column photo_place text;

create index looks_photo_place_idx on looks (created_at) where photo_place is not null;
