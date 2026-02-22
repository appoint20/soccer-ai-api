Build started...
Build succeeded.
The Entity Framework tools version '8.0.2' is older than that of the runtime '9.0.13'. Update the tools for the latest features and bug fixes. See https://aka.ms/AAc1fbw for more information.
BEGIN TRANSACTION;
CREATE TABLE "UserCombinations" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_UserCombinations" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "Status" TEXT NOT NULL,
    "TotalOdds" REAL NOT NULL
);

CREATE TABLE "UserCombinationMatches" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_UserCombinationMatches" PRIMARY KEY AUTOINCREMENT,
    "UserCombinationId" INTEGER NOT NULL,
    "FixtureId" INTEGER NOT NULL,
    "Market" TEXT NOT NULL,
    "Prediction" TEXT NOT NULL,
    "Odds" REAL NOT NULL,
    "Confidence" REAL NOT NULL,
    "Status" TEXT NOT NULL,
    CONSTRAINT "FK_UserCombinationMatches_UserCombinations_UserCombinationId" FOREIGN KEY ("UserCombinationId") REFERENCES "UserCombinations" ("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_UserCombinationMatches_UserCombinationId" ON "UserCombinationMatches" ("UserCombinationId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260222143438_AddUserCombinations', '9.0.13');

COMMIT;


