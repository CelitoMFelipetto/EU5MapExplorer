CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

INSERT INTO "__EFMigrationsHistory" VALUES ('20260327011256_InitialSchema',      '10.0.0') ON CONFLICT DO NOTHING;
INSERT INTO "__EFMigrationsHistory" VALUES ('20260329174746_AddAreaHierarchy',    '10.0.0') ON CONFLICT DO NOTHING;
INSERT INTO "__EFMigrationsHistory" VALUES ('20260401214532_BorderBasedGeometry', '10.0.0') ON CONFLICT DO NOTHING;
