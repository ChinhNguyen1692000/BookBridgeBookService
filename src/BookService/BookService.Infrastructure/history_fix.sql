START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251021043950_AddChatEntitiesFinal') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251021043950_AddChatEntitiesFinal', '8.0.17');
    END IF;
END $EF$;
COMMIT;

