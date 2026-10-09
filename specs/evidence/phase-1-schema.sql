CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
    "ProductVersion" TEXT NOT NULL
);

BEGIN TRANSACTION;

CREATE TABLE "Companies" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Companies" PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "LegalName" TEXT NOT NULL,
    "Gstin" TEXT NULL,
    "StateCode" TEXT NOT NULL,
    "FinancialYearStartMonth" INTEGER NOT NULL,
    "BaseCurrencyCode" TEXT NOT NULL,
    "IsActive" INTEGER NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "CreatedBy" TEXT NOT NULL,
    "ModifiedAtUtc" TEXT NULL,
    "ModifiedBy" TEXT NULL
);

CREATE UNIQUE INDEX "IX_Companies_Gstin" ON "Companies" ("Gstin") WHERE [Gstin] IS NOT NULL;

CREATE UNIQUE INDEX "IX_Companies_Name" ON "Companies" ("Name");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260814023230_InitialCreate', '8.0.10');

COMMIT;

BEGIN TRANSACTION;

CREATE TABLE "AuditLogEntries" (
    "Sequence" INTEGER NOT NULL CONSTRAINT "PK_AuditLogEntries" PRIMARY KEY AUTOINCREMENT,
    "CompanyId" TEXT NULL,
    "OccurredAtUtc" TEXT NOT NULL,
    "UserName" TEXT NOT NULL,
    "MachineName" TEXT NOT NULL,
    "SaveBatchId" TEXT NOT NULL,
    "Action" TEXT NOT NULL,
    "EntityType" TEXT NOT NULL,
    "EntityId" TEXT NOT NULL,
    "AggregateType" TEXT NULL,
    "AggregateId" TEXT NULL,
    "ChangedProperties" TEXT NOT NULL,
    "BeforeJson" TEXT NULL,
    "AfterJson" TEXT NULL,
    "PreviousHash" TEXT NOT NULL,
    "RowHash" TEXT NOT NULL
);

CREATE INDEX "IX_AuditLogEntries_AggregateType_AggregateId" ON "AuditLogEntries" ("AggregateType", "AggregateId");

CREATE INDEX "IX_AuditLogEntries_CompanyId_OccurredAtUtc" ON "AuditLogEntries" ("CompanyId", "OccurredAtUtc");

CREATE INDEX "IX_AuditLogEntries_EntityType_EntityId" ON "AuditLogEntries" ("EntityType", "EntityId");

CREATE INDEX "IX_AuditLogEntries_SaveBatchId" ON "AuditLogEntries" ("SaveBatchId");

CREATE TRIGGER TR_AuditLogEntries_BlockUpdate
BEFORE UPDATE ON AuditLogEntries
BEGIN
    SELECT RAISE(ABORT, 'The audit trail is append-only: audit log rows cannot be updated.');
END;

CREATE TRIGGER TR_AuditLogEntries_BlockDelete
BEFORE DELETE ON AuditLogEntries
BEGIN
    SELECT RAISE(ABORT, 'The audit trail is append-only: audit log rows cannot be deleted.');
END;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260815092544_AddAuditLogTable', '8.0.10');

COMMIT;

BEGIN TRANSACTION;

CREATE TABLE "AccountGroups" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AccountGroups" PRIMARY KEY,
    "CompanyId" TEXT NOT NULL,
    "Name" TEXT NOT NULL,
    "NameNormalised" TEXT NOT NULL,
    "ParentGroupId" TEXT NULL,
    "MaterialisedPath" TEXT NOT NULL,
    "Nature" TEXT NOT NULL,
    "AffectsGrossProfit" INTEGER NOT NULL,
    "IsPrimary" INTEGER NOT NULL,
    "IsSystem" INTEGER NOT NULL,
    "SortOrder" INTEGER NOT NULL,
    "IsActive" INTEGER NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "CreatedBy" TEXT NOT NULL,
    "ModifiedAtUtc" TEXT NULL,
    "ModifiedBy" TEXT NULL,
    CONSTRAINT "FK_AccountGroups_AccountGroups_ParentGroupId" FOREIGN KEY ("ParentGroupId") REFERENCES "AccountGroups" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AccountGroups_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "CostCategories" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_CostCategories" PRIMARY KEY,
    "CompanyId" TEXT NOT NULL,
    "Name" TEXT NOT NULL,
    "NameNormalised" TEXT NOT NULL,
    "IsActive" INTEGER NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "CreatedBy" TEXT NOT NULL,
    "ModifiedAtUtc" TEXT NULL,
    "ModifiedBy" TEXT NULL,
    CONSTRAINT "FK_CostCategories_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "FinancialYears" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_FinancialYears" PRIMARY KEY,
    "CompanyId" TEXT NOT NULL,
    "StartDate" TEXT NOT NULL,
    "EndDate" TEXT NOT NULL,
    "Label" TEXT NOT NULL,
    "IsClosed" INTEGER NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "CreatedBy" TEXT NOT NULL,
    "ModifiedAtUtc" TEXT NULL,
    "ModifiedBy" TEXT NULL,
    CONSTRAINT "FK_FinancialYears_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "PeriodLocks" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_PeriodLocks" PRIMARY KEY,
    "CompanyId" TEXT NOT NULL,
    "LockedThroughDate" TEXT NULL,
    "Reason" TEXT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "CreatedBy" TEXT NOT NULL,
    "ModifiedAtUtc" TEXT NULL,
    "ModifiedBy" TEXT NULL,
    CONSTRAINT "FK_PeriodLocks_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "VoucherTypes" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_VoucherTypes" PRIMARY KEY,
    "CompanyId" TEXT NOT NULL,
    "Name" TEXT NOT NULL,
    "NameNormalised" TEXT NOT NULL,
    "Kind" TEXT NOT NULL,
    "IsSystem" INTEGER NOT NULL,
    "DefaultSeriesId" TEXT NULL,
    "IsActive" INTEGER NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "CreatedBy" TEXT NOT NULL,
    "ModifiedAtUtc" TEXT NULL,
    "ModifiedBy" TEXT NULL,
    CONSTRAINT "FK_VoucherTypes_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "Ledgers" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Ledgers" PRIMARY KEY,
    "CompanyId" TEXT NOT NULL,
    "Name" TEXT NOT NULL,
    "NameNormalised" TEXT NOT NULL,
    "Alias" TEXT NULL,
    "AccountGroupId" TEXT NOT NULL,
    "IsSystem" INTEGER NOT NULL,
    "AllowsManualPosting" INTEGER NOT NULL,
    "Description" TEXT NULL,
    "IsActive" INTEGER NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "CreatedBy" TEXT NOT NULL,
    "ModifiedAtUtc" TEXT NULL,
    "ModifiedBy" TEXT NULL,
    CONSTRAINT "FK_Ledgers_AccountGroups_AccountGroupId" FOREIGN KEY ("AccountGroupId") REFERENCES "AccountGroups" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Ledgers_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "CostCentres" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_CostCentres" PRIMARY KEY,
    "CompanyId" TEXT NOT NULL,
    "Name" TEXT NOT NULL,
    "NameNormalised" TEXT NOT NULL,
    "CostCategoryId" TEXT NULL,
    "ParentCostCentreId" TEXT NULL,
    "MaterialisedPath" TEXT NOT NULL,
    "IsActive" INTEGER NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "CreatedBy" TEXT NOT NULL,
    "ModifiedAtUtc" TEXT NULL,
    "ModifiedBy" TEXT NULL,
    CONSTRAINT "FK_CostCentres_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_CostCentres_CostCategories_CostCategoryId" FOREIGN KEY ("CostCategoryId") REFERENCES "CostCategories" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_CostCentres_CostCentres_ParentCostCentreId" FOREIGN KEY ("ParentCostCentreId") REFERENCES "CostCentres" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "VoucherNumberSeries" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_VoucherNumberSeries" PRIMARY KEY,
    "CompanyId" TEXT NOT NULL,
    "VoucherTypeId" TEXT NOT NULL,
    "Name" TEXT NOT NULL,
    "NameNormalised" TEXT NOT NULL,
    "Prefix" TEXT NOT NULL,
    "Suffix" TEXT NULL,
    "Width" INTEGER NOT NULL,
    "StartNumber" INTEGER NOT NULL,
    "ResetPolicy" TEXT NOT NULL,
    "AllowManualOverride" INTEGER NOT NULL,
    "IsActive" INTEGER NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "CreatedBy" TEXT NOT NULL,
    "ModifiedAtUtc" TEXT NULL,
    "ModifiedBy" TEXT NULL,
    CONSTRAINT "FK_VoucherNumberSeries_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_VoucherNumberSeries_VoucherTypes_VoucherTypeId" FOREIGN KEY ("VoucherTypeId") REFERENCES "VoucherTypes" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "VoucherNumberSequences" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_VoucherNumberSequences" PRIMARY KEY,
    "SeriesId" TEXT NOT NULL,
    "FinancialYearId" TEXT NOT NULL,
    "LastAllocatedNumber" INTEGER NOT NULL,
    CONSTRAINT "FK_VoucherNumberSequences_FinancialYears_FinancialYearId" FOREIGN KEY ("FinancialYearId") REFERENCES "FinancialYears" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_VoucherNumberSequences_VoucherNumberSeries_SeriesId" FOREIGN KEY ("SeriesId") REFERENCES "VoucherNumberSeries" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "Vouchers" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Vouchers" PRIMARY KEY,
    "CompanyId" TEXT NOT NULL,
    "VoucherTypeId" TEXT NOT NULL,
    "SeriesId" TEXT NOT NULL,
    "FinancialYearId" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "CreatedBy" TEXT NOT NULL,
    "ModifiedAtUtc" TEXT NULL,
    "ModifiedBy" TEXT NULL,
    CONSTRAINT "FK_Vouchers_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Vouchers_FinancialYears_FinancialYearId" FOREIGN KEY ("FinancialYearId") REFERENCES "FinancialYears" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Vouchers_VoucherNumberSeries_SeriesId" FOREIGN KEY ("SeriesId") REFERENCES "VoucherNumberSeries" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Vouchers_VoucherTypes_VoucherTypeId" FOREIGN KEY ("VoucherTypeId") REFERENCES "VoucherTypes" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "VoucherRevisions" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_VoucherRevisions" PRIMARY KEY,
    "CompanyId" TEXT NOT NULL,
    "VoucherId" TEXT NOT NULL,
    "VoucherTypeId" TEXT NOT NULL,
    "SeriesId" TEXT NOT NULL,
    "FinancialYearId" TEXT NOT NULL,
    "RevisionNumber" INTEGER NOT NULL,
    "Status" TEXT NOT NULL,
    "VoucherDate" TEXT NOT NULL,
    "Number" TEXT NOT NULL,
    "NumberNormalised" TEXT NOT NULL,
    "IsManualNumber" INTEGER NOT NULL,
    "Narration" TEXT NULL,
    "ReferenceNumber" TEXT NULL,
    "ReferenceDate" TEXT NULL,
    "ChangeReason" TEXT NULL,
    "SupersededByRevisionId" TEXT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "CreatedBy" TEXT NOT NULL,
    CONSTRAINT "FK_VoucherRevisions_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_VoucherRevisions_FinancialYears_FinancialYearId" FOREIGN KEY ("FinancialYearId") REFERENCES "FinancialYears" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_VoucherRevisions_VoucherNumberSeries_SeriesId" FOREIGN KEY ("SeriesId") REFERENCES "VoucherNumberSeries" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_VoucherRevisions_VoucherTypes_VoucherTypeId" FOREIGN KEY ("VoucherTypeId") REFERENCES "VoucherTypes" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_VoucherRevisions_Vouchers_VoucherId" FOREIGN KEY ("VoucherId") REFERENCES "Vouchers" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "JournalLines" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_JournalLines" PRIMARY KEY,
    "CompanyId" TEXT NOT NULL,
    "VoucherRevisionId" TEXT NOT NULL,
    "LineNumber" INTEGER NOT NULL,
    "LedgerId" TEXT NOT NULL,
    "AmountMinor" INTEGER NOT NULL,
    "CostCentreId" TEXT NULL,
    "Narration" TEXT NULL,
    CONSTRAINT "FK_JournalLines_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_JournalLines_CostCentres_CostCentreId" FOREIGN KEY ("CostCentreId") REFERENCES "CostCentres" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_JournalLines_Ledgers_LedgerId" FOREIGN KEY ("LedgerId") REFERENCES "Ledgers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_JournalLines_VoucherRevisions_VoucherRevisionId" FOREIGN KEY ("VoucherRevisionId") REFERENCES "VoucherRevisions" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "BankReconciliationMarks" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_BankReconciliationMarks" PRIMARY KEY,
    "CompanyId" TEXT NOT NULL,
    "JournalLineId" TEXT NOT NULL,
    "BankDate" TEXT NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "CreatedBy" TEXT NOT NULL,
    "ModifiedAtUtc" TEXT NULL,
    "ModifiedBy" TEXT NULL,
    CONSTRAINT "FK_BankReconciliationMarks_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_BankReconciliationMarks_JournalLines_JournalLineId" FOREIGN KEY ("JournalLineId") REFERENCES "JournalLines" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_AccountGroups_CompanyId_MaterialisedPath" ON "AccountGroups" ("CompanyId", "MaterialisedPath");

CREATE UNIQUE INDEX "IX_AccountGroups_CompanyId_ParentGroupId_NameNormalised" ON "AccountGroups" ("CompanyId", "ParentGroupId", "NameNormalised");

CREATE INDEX "IX_AccountGroups_ParentGroupId" ON "AccountGroups" ("ParentGroupId");

CREATE INDEX "IX_BankReconciliationMarks_CompanyId" ON "BankReconciliationMarks" ("CompanyId");

CREATE UNIQUE INDEX "IX_BankReconciliationMarks_JournalLineId" ON "BankReconciliationMarks" ("JournalLineId");

CREATE INDEX "IX_CostCategories_CompanyId" ON "CostCategories" ("CompanyId");

CREATE INDEX "IX_CostCentres_CompanyId" ON "CostCentres" ("CompanyId");

CREATE INDEX "IX_CostCentres_CostCategoryId" ON "CostCentres" ("CostCategoryId");

CREATE INDEX "IX_CostCentres_ParentCostCentreId" ON "CostCentres" ("ParentCostCentreId");

CREATE UNIQUE INDEX "IX_FinancialYears_CompanyId_StartDate" ON "FinancialYears" ("CompanyId", "StartDate");

CREATE INDEX "IX_JournalLines_CompanyId_LedgerId" ON "JournalLines" ("CompanyId", "LedgerId");

CREATE INDEX "IX_JournalLines_CostCentreId" ON "JournalLines" ("CostCentreId");

CREATE INDEX "IX_JournalLines_LedgerId" ON "JournalLines" ("LedgerId");

CREATE INDEX "IX_JournalLines_VoucherRevisionId_LineNumber" ON "JournalLines" ("VoucherRevisionId", "LineNumber");

CREATE INDEX "IX_Ledgers_AccountGroupId" ON "Ledgers" ("AccountGroupId");

CREATE UNIQUE INDEX "IX_Ledgers_CompanyId_NameNormalised" ON "Ledgers" ("CompanyId", "NameNormalised");

CREATE UNIQUE INDEX "IX_PeriodLocks_CompanyId" ON "PeriodLocks" ("CompanyId");

CREATE INDEX "IX_VoucherNumberSequences_FinancialYearId" ON "VoucherNumberSequences" ("FinancialYearId");

CREATE UNIQUE INDEX "IX_VoucherNumberSequences_SeriesId_FinancialYearId" ON "VoucherNumberSequences" ("SeriesId", "FinancialYearId");

CREATE INDEX "IX_VoucherNumberSeries_CompanyId" ON "VoucherNumberSeries" ("CompanyId");

CREATE INDEX "IX_VoucherNumberSeries_VoucherTypeId" ON "VoucherNumberSeries" ("VoucherTypeId");

CREATE UNIQUE INDEX "IX_VoucherRevisions_CompanyId_VoucherTypeId_SeriesId_FinancialYearId_NumberNormalised" ON "VoucherRevisions" ("CompanyId", "VoucherTypeId", "SeriesId", "FinancialYearId", "NumberNormalised") WHERE [SupersededByRevisionId] IS NULL;

CREATE INDEX "IX_VoucherRevisions_FinancialYearId" ON "VoucherRevisions" ("FinancialYearId");

CREATE INDEX "IX_VoucherRevisions_SeriesId" ON "VoucherRevisions" ("SeriesId");

CREATE UNIQUE INDEX "IX_VoucherRevisions_VoucherId_RevisionNumber" ON "VoucherRevisions" ("VoucherId", "RevisionNumber");

CREATE INDEX "IX_VoucherRevisions_VoucherTypeId" ON "VoucherRevisions" ("VoucherTypeId");

CREATE INDEX "IX_Vouchers_CompanyId" ON "Vouchers" ("CompanyId");

CREATE INDEX "IX_Vouchers_FinancialYearId" ON "Vouchers" ("FinancialYearId");

CREATE INDEX "IX_Vouchers_SeriesId" ON "Vouchers" ("SeriesId");

CREATE INDEX "IX_Vouchers_VoucherTypeId" ON "Vouchers" ("VoucherTypeId");

CREATE UNIQUE INDEX "IX_VoucherTypes_CompanyId_NameNormalised" ON "VoucherTypes" ("CompanyId", "NameNormalised");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260815095651_AddAccountingCore', '8.0.10');

COMMIT;

