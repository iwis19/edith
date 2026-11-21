alter table memories add column collected_on date;
update memories set collected_on = created_at::date;
alter table memories alter column collected_on set not null;
drop index memories_profile_created_idx;
alter table memories drop column kind;
alter table memories drop column created_at;
create index memories_profile_collected_idx on memories (profile_id, collected_on desc, id desc);
