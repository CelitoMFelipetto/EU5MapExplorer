ALTER TABLE areas ADD COLUMN IF NOT EXISTS "Continent" character varying(128);
ALTER TABLE areas ADD COLUMN IF NOT EXISTS "SubContinent" character varying(128);
ALTER TABLE areas ADD COLUMN IF NOT EXISTS "Region" character varying(128);
