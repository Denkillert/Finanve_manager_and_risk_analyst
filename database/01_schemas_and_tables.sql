-- ============================================================================
-- Сетевое программное средство управления финансами проектов и анализа рисков
-- СУБД: Microsoft SQL Server 2022
-- Схемы: finance, risk, security, audit
-- Соответствует моделям раздела 2.2 (таблицы 2.2, 2.3, рис. 2.10) и правилам 1–16
-- ============================================================================

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'finance') EXEC(N'CREATE SCHEMA finance');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'risk') EXEC(N'CREATE SCHEMA risk');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'security') EXEC(N'CREATE SCHEMA security');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'audit') EXEC(N'CREATE SCHEMA audit');
GO

-- ----------------------------------------------------------------------------
-- Схема security: роли и пользователи (НФТ-7, НФТ-8, НФТ-10)
-- ----------------------------------------------------------------------------
CREATE TABLE security.Roles (
    Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Roles PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    Code NVARCHAR(50) NOT NULL,
    Name NVARCHAR(150) NOT NULL,
    ClientType NVARCHAR(20) NOT NULL CONSTRAINT CK_Roles_ClientType CHECK (ClientType IN (N'WPF', N'Web', N'Both')),
    Description NVARCHAR(500) NULL,
    CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    IsDeleted BIT NOT NULL DEFAULT 0,
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT UQ_Roles_Code UNIQUE (Code)
);

CREATE TABLE security.Users (
    Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Users PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    RoleId UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Users_Role FOREIGN KEY REFERENCES security.Roles(Id),
    Login NVARCHAR(100) NOT NULL,
    PasswordHash NVARCHAR(500) NOT NULL,
    FullName NVARCHAR(200) NOT NULL,
    Email NVARCHAR(200) NOT NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    FailedLoginCount INT NOT NULL DEFAULT 0 CONSTRAINT CK_Users_FailedCount CHECK (FailedLoginCount >= 0),
    LockedUntil DATETIME2(3) NULL,
    TwoFactorEnabled BIT NOT NULL DEFAULT 0,
    TwoFactorSecret NVARCHAR(100) NULL,
    PersonalDataConsentGiven BIT NOT NULL DEFAULT 0,
    ConsentDate DATETIME2(3) NULL,
    CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    IsDeleted BIT NOT NULL DEFAULT 0,
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT UQ_Users_Login UNIQUE (Login)
);

-- ----------------------------------------------------------------------------
-- Схема finance: организационная иерархия, бюджеты, договоры, заявки, платежи
-- ----------------------------------------------------------------------------
CREATE TABLE finance.Organizations (
    Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Organizations PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    Name NVARCHAR(250) NOT NULL,
    Unp NVARCHAR(20) NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    IsDeleted BIT NOT NULL DEFAULT 0,
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT UQ_Organizations_Unp UNIQUE (Unp)
);

CREATE TABLE finance.Portfolios (
    Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Portfolios PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    OrganizationId UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Portfolios_Organization FOREIGN KEY REFERENCES finance.Organizations(Id),
    Name NVARCHAR(250) NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    IsDeleted BIT NOT NULL DEFAULT 0,
    RowVersion ROWVERSION NOT NULL
);

CREATE TABLE finance.Projects (
    Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Projects PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    PortfolioId UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Projects_Portfolio FOREIGN KEY REFERENCES finance.Portfolios(Id),
    Code NVARCHAR(50) NOT NULL,
    Name NVARCHAR(300) NOT NULL,
    ContractValue DECIMAL(18, 4) NOT NULL CONSTRAINT CK_Projects_ContractValue CHECK (ContractValue >= 0),
    Currency CHAR(3) NOT NULL DEFAULT 'BYN',
    InitialCash DECIMAL(18, 4) NOT NULL DEFAULT 0 CONSTRAINT CK_Projects_InitialCash CHECK (InitialCash >= 0),
    MinCashBalance DECIMAL(18, 4) NOT NULL DEFAULT 0 CONSTRAINT CK_Projects_MinCash CHECK (MinCashBalance >= 0),
    PlannedStartDate DATE NOT NULL,
    PlannedEndDate DATE NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    IsDeleted BIT NOT NULL DEFAULT 0,
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT UQ_Projects_Portfolio_Code UNIQUE (PortfolioId, Code),
    CONSTRAINT CK_Projects_Dates CHECK (PlannedEndDate >= PlannedStartDate)
);

CREATE TABLE finance.ConstructionObjects (
    Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ConstructionObjects PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    ProjectId UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_ConstructionObjects_Project FOREIGN KEY REFERENCES finance.Projects(Id),
    QueueName NVARCHAR(100) NOT NULL DEFAULT N'1-я очередь',
    Name NVARCHAR(250) NOT NULL,
    Address NVARCHAR(300) NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    IsDeleted BIT NOT NULL DEFAULT 0,
    RowVersion ROWVERSION NOT NULL
);

CREATE TABLE finance.WorkStages (
    Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_WorkStages PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    ConstructionObjectId UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_WorkStages_Object FOREIGN KEY REFERENCES finance.ConstructionObjects(Id),
    Name NVARCHAR(250) NOT NULL,
    PlannedStartDate DATE NOT NULL,
    PlannedEndDate DATE NOT NULL,
    PlannedValuePV DECIMAL(18, 4) NOT NULL DEFAULT 0 CONSTRAINT CK_WorkStages_PV CHECK (PlannedValuePV >= 0),
    EarnedValueEV DECIMAL(18, 4) NOT NULL DEFAULT 0 CONSTRAINT CK_WorkStages_EV CHECK (EarnedValueEV >= 0),
    ActualCostAC DECIMAL(18, 4) NOT NULL DEFAULT 0 CONSTRAINT CK_WorkStages_AC CHECK (ActualCostAC >= 0),
    CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    IsDeleted BIT NOT NULL DEFAULT 0,
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT CK_WorkStages_Dates CHECK (PlannedEndDate >= PlannedStartDate)
);

CREATE TABLE finance.CostItems (
    Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_CostItems PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    Code NVARCHAR(50) NOT NULL,
    Name NVARCHAR(250) NOT NULL,
    Category NVARCHAR(100) NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    IsDeleted BIT NOT NULL DEFAULT 0,
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT UQ_CostItems_Code UNIQUE (Code)
);

CREATE TABLE finance.BudgetVersions (
    Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_BudgetVersions PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    ProjectId UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_BudgetVersions_Project FOREIGN KEY REFERENCES finance.Projects(Id),
    VersionNumber INT NOT NULL CONSTRAINT CK_BudgetVersions_Number CHECK (VersionNumber >= 1),
    Status NVARCHAR(30) NOT NULL CONSTRAINT CK_BudgetVersions_Status CHECK (Status IN (N'Draft', N'Approved', N'Archived')),
    ApprovedAt DATETIME2(3) NULL,
    ApprovedByUserId UNIQUEIDENTIFIER NULL CONSTRAINT FK_BudgetVersions_User FOREIGN KEY REFERENCES security.Users(Id),
    Comment NVARCHAR(500) NULL,
    CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    IsDeleted BIT NOT NULL DEFAULT 0,
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT UQ_BudgetVersions_Project_Num UNIQUE (ProjectId, VersionNumber)
);

CREATE TABLE finance.BudgetLimits (
    Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_BudgetLimits PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    BudgetVersionId UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_BudgetLimits_Version FOREIGN KEY REFERENCES finance.BudgetVersions(Id),
    ConstructionObjectId UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_BudgetLimits_Object FOREIGN KEY REFERENCES finance.ConstructionObjects(Id),
    WorkStageId UNIQUEIDENTIFIER NULL CONSTRAINT FK_BudgetLimits_Stage FOREIGN KEY REFERENCES finance.WorkStages(Id),
    CostItemId UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_BudgetLimits_CostItem FOREIGN KEY REFERENCES finance.CostItems(Id),
    PeriodStart DATE NOT NULL,
    PeriodEnd DATE NOT NULL,
    Amount DECIMAL(18, 4) NOT NULL CONSTRAINT CK_BudgetLimits_Amount CHECK (Amount >= 0),
    CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    IsDeleted BIT NOT NULL DEFAULT 0,
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT CK_BudgetLimits_Period CHECK (PeriodEnd >= PeriodStart)
);

CREATE TABLE finance.Contracts (
    Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Contracts PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    ProjectId UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Contracts_Project FOREIGN KEY REFERENCES finance.Projects(Id),
    Number NVARCHAR(80) NOT NULL,
    Counterparty NVARCHAR(250) NOT NULL,
    CounterpartyUnp NVARCHAR(20) NOT NULL,
    Direction NVARCHAR(40) NOT NULL CONSTRAINT CK_Contracts_Direction CHECK (Direction IN (N'CustomerIncoming', N'SupplierSubcontractorOutgoing')),
    TotalAmount DECIMAL(18, 4) NOT NULL CONSTRAINT CK_Contracts_Amount CHECK (TotalAmount >= 0),
    VatIncluded BIT NOT NULL DEFAULT 1,
    VatRate DECIMAL(5, 2) NOT NULL DEFAULT 20.00,
    Currency CHAR(3) NOT NULL DEFAULT 'BYN',
    StartDate DATE NOT NULL,
    EndDate DATE NOT NULL,
    RetentionPercent DECIMAL(5, 2) NOT NULL DEFAULT 5.00 CONSTRAINT CK_Contracts_Retention CHECK (RetentionPercent BETWEEN 0 AND 100),
    PenaltyRatePerDay DECIMAL(6, 4) NOT NULL DEFAULT 0.1000,
    MaxPenaltyPercent DECIMAL(5, 2) NOT NULL DEFAULT 10.00,
    SpecificationNotes NVARCHAR(1000) NULL,
    CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    IsDeleted BIT NOT NULL DEFAULT 0,
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT UQ_Contracts_Project_Number UNIQUE (ProjectId, Number),
    CONSTRAINT CK_Contracts_Dates CHECK (EndDate >= StartDate)
);

CREATE TABLE finance.ContractSupplements (
    Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ContractSupplements PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    ContractId UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Supplements_Contract FOREIGN KEY REFERENCES finance.Contracts(Id),
    Number NVARCHAR(50) NOT NULL,
    AmountDelta DECIMAL(18, 4) NOT NULL,
    SignedDate DATE NOT NULL,
    Reason NVARCHAR(500) NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    IsDeleted BIT NOT NULL DEFAULT 0,
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT UQ_ContractSupplements_Number UNIQUE (ContractId, Number)
);

CREATE TABLE finance.ContractScheduleItems (
    Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ContractScheduleItems PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    ContractId UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Schedule_Contract FOREIGN KEY REFERENCES finance.Contracts(Id),
    PlannedDate DATE NOT NULL,
    Amount DECIMAL(18, 4) NOT NULL CONSTRAINT CK_Schedule_Amount CHECK (Amount >= 0),
    IsIncoming BIT NOT NULL,
    IsPaid BIT NOT NULL DEFAULT 0,
    Description NVARCHAR(300) NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    IsDeleted BIT NOT NULL DEFAULT 0,
    RowVersion ROWVERSION NOT NULL
);

CREATE TABLE finance.Obligations (
    Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Obligations PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    ContractId UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Obligations_Contract FOREIGN KEY REFERENCES finance.Contracts(Id),
    ConstructionObjectId UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Obligations_Object FOREIGN KEY REFERENCES finance.ConstructionObjects(Id),
    CostItemId UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Obligations_CostItem FOREIGN KEY REFERENCES finance.CostItems(Id),
    GrossAmount DECIMAL(18, 4) NOT NULL CONSTRAINT CK_Obligations_Gross CHECK (GrossAmount >= 0),
    PaidAmount DECIMAL(18, 4) NOT NULL DEFAULT 0 CONSTRAINT CK_Obligations_Paid CHECK (PaidAmount >= 0),
    Status NVARCHAR(30) NOT NULL CONSTRAINT CK_Obligations_Status CHECK (Status IN (N'Open', N'PartiallyPaid', N'Closed')),
    CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    IsDeleted BIT NOT NULL DEFAULT 0,
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT CK_Obligations_PaidLteGross CHECK (PaidAmount <= GrossAmount)
);

CREATE TABLE finance.Advances (
    Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Advances PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    ContractId UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Advances_Contract FOREIGN KEY REFERENCES finance.Contracts(Id),
    ConstructionObjectId UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Advances_Object FOREIGN KEY REFERENCES finance.ConstructionObjects(Id),
    CostItemId UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Advances_CostItem FOREIGN KEY REFERENCES finance.CostItems(Id),
    Amount DECIMAL(18, 4) NOT NULL CONSTRAINT CK_Advances_Amount CHECK (Amount > 0),
    UsedAmount DECIMAL(18, 4) NOT NULL DEFAULT 0 CONSTRAINT CK_Advances_Used CHECK (UsedAmount >= 0),
    IssuedDate DATE NOT NULL,
    DueDate DATE NOT NULL,
    Status NVARCHAR(30) NOT NULL CONSTRAINT CK_Advances_Status CHECK (Status IN (N'Active', N'Overdue', N'Closed')),
    FundingSource NVARCHAR(200) NOT NULL DEFAULT N'Собственные средства',
    CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    IsDeleted BIT NOT NULL DEFAULT 0,
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT CK_Advances_UsedLteAmount CHECK (UsedAmount <= Amount)
);

CREATE TABLE finance.PaymentRequests (
    Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_PaymentRequests PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    ProjectId UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Requests_Project FOREIGN KEY REFERENCES finance.Projects(Id),
    ConstructionObjectId UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Requests_Object FOREIGN KEY REFERENCES finance.ConstructionObjects(Id),
    ContractId UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Requests_Contract FOREIGN KEY REFERENCES finance.Contracts(Id),
    CostItemId UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Requests_CostItem FOREIGN KEY REFERENCES finance.CostItems(Id),
    BudgetVersionId UNIQUEIDENTIFIER NULL CONSTRAINT FK_Requests_BudgetVersion FOREIGN KEY REFERENCES finance.BudgetVersions(Id),
    AuthorUserId UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Requests_Author FOREIGN KEY REFERENCES security.Users(Id),
    Number NVARCHAR(60) NOT NULL,
    Amount DECIMAL(18, 4) NOT NULL CONSTRAINT CK_Requests_Amount CHECK (Amount > 0),
    ActAmount DECIMAL(18, 4) NOT NULL DEFAULT 0 CONSTRAINT CK_Requests_ActAmount CHECK (ActAmount >= 0),
    AdvanceOffset DECIMAL(18, 4) NOT NULL DEFAULT 0 CONSTRAINT CK_Requests_AdvOffset CHECK (AdvanceOffset >= 0),
    RetentionAmount DECIMAL(18, 4) NOT NULL DEFAULT 0 CONSTRAINT CK_Requests_Retention CHECK (RetentionAmount >= 0),
    PayableAmount DECIMAL(18, 4) NOT NULL CONSTRAINT CK_Requests_Payable CHECK (PayableAmount >= 0),
    VatIncluded BIT NOT NULL DEFAULT 1,
    VatRate DECIMAL(5, 2) NOT NULL DEFAULT 20.00,
    VatAmount DECIMAL(18, 4) NOT NULL DEFAULT 0,
    Currency CHAR(3) NOT NULL DEFAULT 'BYN',
    ExchangeRate DECIMAL(18, 6) NOT NULL DEFAULT 1.000000,
    AmountInByn DECIMAL(18, 4) NOT NULL CONSTRAINT CK_Requests_Byn CHECK (AmountInByn >= 0),
    Type NVARCHAR(30) NOT NULL CONSTRAINT CK_Requests_Type CHECK (Type IN (N'Advance', N'CurrentPayment')),
    Status NVARCHAR(40) NOT NULL CONSTRAINT CK_Requests_Status CHECK (
        Status IN (N'Draft', N'UnderReview', N'Returned', N'Blocked', N'PendingApproval', N'ApprovedByManagement', N'Approved', N'Executed', N'Closed', N'Rejected')
    ),
    PlannedPaymentDate DATE NOT NULL,
    Purpose NVARCHAR(500) NULL,
    BlockReason NVARCHAR(500) NULL,
    ReturnOrRejectReason NVARCHAR(500) NULL,
    OverrunJustification NVARCHAR(1000) NULL,
    HasOverdueAdvanceWarning BIT NOT NULL DEFAULT 0,
    IsOverrunApproved BIT NOT NULL DEFAULT 0,
    EdsSignatureThumbprint NVARCHAR(200) NULL,
    EdsSignedAt DATETIME2(3) NULL,
    IdempotencyKey NVARCHAR(120) NULL,
    SubmittedAt DATETIME2(3) NULL,
    BlockedAt DATETIME2(3) NULL,
    ApprovedAt DATETIME2(3) NULL,
    ExecutedAt DATETIME2(3) NULL,
    ClosedAt DATETIME2(3) NULL,
    CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    IsDeleted BIT NOT NULL DEFAULT 0,
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT UQ_PaymentRequests_Number UNIQUE (Number),
    CONSTRAINT CK_Requests_OffsetRetention CHECK (AdvanceOffset + RetentionAmount <= Amount)
);

CREATE TABLE finance.Documents (
    Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Documents PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    PaymentRequestId UNIQUEIDENTIFIER NULL CONSTRAINT FK_Documents_Request FOREIGN KEY REFERENCES finance.PaymentRequests(Id),
    ContractId UNIQUEIDENTIFIER NULL CONSTRAINT FK_Documents_Contract FOREIGN KEY REFERENCES finance.Contracts(Id),
    ConstructionObjectId UNIQUEIDENTIFIER NULL CONSTRAINT FK_Documents_Object FOREIGN KEY REFERENCES finance.ConstructionObjects(Id),
    Type NVARCHAR(30) NOT NULL CONSTRAINT CK_Documents_Type CHECK (Type IN (N'Contract', N'ActS2', N'CertificateS3', N'Invoice', N'BankStatement', N'Specification')),
    Number NVARCHAR(80) NOT NULL,
    Date DATE NOT NULL,
    GrossAmount DECIMAL(18, 4) NOT NULL DEFAULT 0 CHECK (GrossAmount >= 0),
    NetAmount DECIMAL(18, 4) NOT NULL DEFAULT 0 CHECK (NetAmount >= 0),
    VatIncluded BIT NOT NULL DEFAULT 1,
    Currency CHAR(3) NOT NULL DEFAULT 'BYN',
    AdvanceOffset DECIMAL(18, 4) NOT NULL DEFAULT 0,
    RetentionAmount DECIMAL(18, 4) NOT NULL DEFAULT 0,
    FilePath NVARCHAR(500) NULL,
    PhotoEvidencePaths NVARCHAR(2000) NULL,
    ActWorkDescription NVARCHAR(1000) NULL,
    ContractorSigner NVARCHAR(200) NULL,
    CustomerSigner NVARCHAR(200) NULL,
    IsVerifiedByInstruction53 BIT NOT NULL DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    IsDeleted BIT NOT NULL DEFAULT 0,
    RowVersion ROWVERSION NOT NULL,
    -- Бизнес-правило 16 и требование 23: один документ-основание оплачивается один раз
    CONSTRAINT UQ_Documents_Type_Number_Date UNIQUE (Type, Number, Date)
);

CREATE TABLE finance.Payments (
    Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Payments PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    PaymentRequestId UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Payments_Request FOREIGN KEY REFERENCES finance.PaymentRequests(Id),
    BankOperationId NVARCHAR(120) NOT NULL,
    PaymentDate DATE NOT NULL,
    Amount DECIMAL(18, 4) NOT NULL CONSTRAINT CK_Payments_Amount CHECK (Amount > 0),
    Currency CHAR(3) NOT NULL DEFAULT 'BYN',
    ExchangeRate DECIMAL(18, 6) NOT NULL DEFAULT 1.000000,
    AmountInByn DECIMAL(18, 4) NOT NULL CONSTRAINT CK_Payments_Byn CHECK (AmountInByn > 0),
    FxDifference DECIMAL(18, 4) NOT NULL DEFAULT 0,
    Status NVARCHAR(30) NOT NULL CONSTRAINT CK_Payments_Status CHECK (Status IN (N'Executed', N'Reconciled')),
    PaymentOrderNumber NVARCHAR(80) NULL,
    CounterpartyAccount NVARCHAR(80) NULL,
    CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    IsDeleted BIT NOT NULL DEFAULT 0,
    RowVersion ROWVERSION NOT NULL,
    -- Бизнес-правило 16 и требование 20: повторная загрузка выписки не создаёт дубль
    CONSTRAINT UQ_Payments_BankOperationId UNIQUE (BankOperationId)
);

CREATE TABLE finance.CurrencyRates (
    Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_CurrencyRates PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    Currency CHAR(3) NOT NULL,
    RateDate DATE NOT NULL,
    RateToByn DECIMAL(18, 6) NOT NULL CONSTRAINT CK_CurrencyRates_Rate CHECK (RateToByn > 0),
    CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    IsDeleted BIT NOT NULL DEFAULT 0,
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT UQ_CurrencyRates_Currency_Date UNIQUE (Currency, RateDate)
);

CREATE TABLE finance.NormRules (
    Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_NormRules PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    Code NVARCHAR(60) NOT NULL,
    Name NVARCHAR(250) NOT NULL,
    DocumentSource NVARCHAR(300) NOT NULL,
    ValueText NVARCHAR(300) NOT NULL,
    NumericValue DECIMAL(18, 6) NULL,
    ValidFrom DATE NOT NULL,
    ValidTo DATE NULL,
    ResponsibleRole NVARCHAR(100) NOT NULL,
    Revision INT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    IsDeleted BIT NOT NULL DEFAULT 0,
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT UQ_NormRules_Code_Revision UNIQUE (Code, Revision)
);

-- ----------------------------------------------------------------------------
-- Схема risk: реестр рисков и версионированные оценки (СТБ ISO 31000-2020)
-- ----------------------------------------------------------------------------
CREATE TABLE risk.Risks (
    Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Risks PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    ProjectId UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Risks_Project FOREIGN KEY REFERENCES finance.Projects(Id),
    Code NVARCHAR(30) NOT NULL,
    Event NVARCHAR(300) NOT NULL,
    Cause NVARCHAR(500) NOT NULL,
    Consequence NVARCHAR(500) NOT NULL,
    Probability DECIMAL(6, 4) NOT NULL CONSTRAINT CK_Risks_Probability CHECK (Probability BETWEEN 0 AND 1),
    MonetaryImpact DECIMAL(18, 4) NOT NULL CONSTRAINT CK_Risks_Impact CHECK (MonetaryImpact >= 0),
    ProbabilityScore INT NOT NULL CONSTRAINT CK_Risks_ProbScore CHECK (ProbabilityScore BETWEEN 1 AND 5),
    ImpactScore INT NOT NULL CONSTRAINT CK_Risks_ImpactScore CHECK (ImpactScore BETWEEN 1 AND 5),
    IntegralScore INT NOT NULL CONSTRAINT CK_Risks_Integral CHECK (IntegralScore BETWEEN 1 AND 25),
    Emv DECIMAL(18, 4) NOT NULL CONSTRAINT CK_Risks_Emv CHECK (Emv >= 0),
    OwnerRole NVARCHAR(150) NOT NULL,
    Mitigation NVARCHAR(500) NOT NULL,
    EarlyIndicator NVARCHAR(300) NOT NULL,
    TriggerFired BIT NOT NULL DEFAULT 0,
    ResidualProbability DECIMAL(6, 4) NULL CONSTRAINT CK_Risks_ResProb CHECK (ResidualProbability IS NULL OR ResidualProbability BETWEEN 0 AND 1),
    ResidualEmv DECIMAL(18, 4) NULL,
    Status NVARCHAR(30) NOT NULL CONSTRAINT CK_Risks_Status CHECK (Status IN (N'Identified', N'Analyzed', N'Treating', N'Monitoring', N'Realized', N'Closed')),
    LinkedContractId UNIQUEIDENTIFIER NULL CONSTRAINT FK_Risks_Contract FOREIGN KEY REFERENCES finance.Contracts(Id),
    LinkedCostItemId UNIQUEIDENTIFIER NULL CONSTRAINT FK_Risks_CostItem FOREIGN KEY REFERENCES finance.CostItems(Id),
    CurrentAssessmentVersion INT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    IsDeleted BIT NOT NULL DEFAULT 0,
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT UQ_Risks_Project_Code UNIQUE (ProjectId, Code)
);

CREATE TABLE risk.RiskAssessments (
    Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_RiskAssessments PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    RiskId UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_RiskAssessments_Risk FOREIGN KEY REFERENCES risk.Risks(Id),
    Version INT NOT NULL,
    Probability DECIMAL(6, 4) NOT NULL CONSTRAINT CK_RiskAssessments_Prob CHECK (Probability BETWEEN 0 AND 1),
    MonetaryImpact DECIMAL(18, 4) NOT NULL CONSTRAINT CK_RiskAssessments_Impact CHECK (MonetaryImpact >= 0),
    Emv DECIMAL(18, 4) NOT NULL,
    ResidualProbability DECIMAL(6, 4) NULL,
    ResidualEmv DECIMAL(18, 4) NULL,
    ProjectReserveP80Snapshot DECIMAL(18, 4) NOT NULL,
    AssessedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    AssessedByUserId UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_RiskAssessments_User FOREIGN KEY REFERENCES security.Users(Id),
    Comment NVARCHAR(500) NULL,
    CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    IsDeleted BIT NOT NULL DEFAULT 0,
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT UQ_RiskAssessments_Version UNIQUE (RiskId, Version)
);

-- ----------------------------------------------------------------------------
-- Схема audit: неизменяемый журнал аудита (хранение 5 лет) и уведомления
-- ----------------------------------------------------------------------------
CREATE TABLE audit.AuditLogs (
    Id BIGINT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_AuditLogs PRIMARY KEY,
    UserId UNIQUEIDENTIFIER NULL,
    UserLogin NVARCHAR(100) NOT NULL,
    Timestamp DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    ObjectName NVARCHAR(150) NOT NULL,
    ObjectId NVARCHAR(100) NOT NULL,
    Action NVARCHAR(100) NOT NULL,
    OldValue NVARCHAR(MAX) NULL,
    NewValue NVARCHAR(MAX) NULL,
    Details NVARCHAR(1000) NULL
);

CREATE TABLE audit.Notifications (
    Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Notifications PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    TargetUserId UNIQUEIDENTIFIER NULL,
    TargetRoleCode NVARCHAR(50) NULL,
    Title NVARCHAR(200) NOT NULL,
    Message NVARCHAR(1000) NOT NULL,
    Severity NVARCHAR(20) NOT NULL CONSTRAINT CK_Notifications_Severity CHECK (Severity IN (N'Info', N'Warning', N'Critical')),
    Category NVARCHAR(40) NOT NULL CONSTRAINT CK_Notifications_Category CHECK (Category IN (N'RiskTrigger', N'CashGap', N'ApprovalDue', N'OverdueAdvance', N'System')),
    IsRead BIT NOT NULL DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    EntityLink NVARCHAR(200) NULL
);
GO

-- ----------------------------------------------------------------------------
-- Индексы производительности (НФТ-2, НФТ-3: до 500 000 операций за 5 лет)
-- ----------------------------------------------------------------------------
CREATE INDEX IX_BudgetLimits_Version_Object_Item ON finance.BudgetLimits (BudgetVersionId, ConstructionObjectId, CostItemId) INCLUDE (Amount);
CREATE INDEX IX_Obligations_Object_Item ON finance.Obligations (ConstructionObjectId, CostItemId) INCLUDE (GrossAmount, PaidAmount, Status);
CREATE INDEX IX_PaymentRequests_Status_Date ON finance.PaymentRequests (Status, PlannedPaymentDate) INCLUDE (ProjectId, ConstructionObjectId, CostItemId, AmountInByn);
CREATE INDEX IX_PaymentRequests_Contract ON finance.PaymentRequests (ContractId, Status);
CREATE INDEX IX_Payments_Date ON finance.Payments (PaymentDate) INCLUDE (AmountInByn, PaymentRequestId);
CREATE INDEX IX_AuditLogs_Timestamp_Object ON audit.AuditLogs (Timestamp DESC, ObjectName);
CREATE INDEX IX_Risks_Project_Score ON risk.Risks (ProjectId, Emv DESC, IntegralScore DESC);
GO

-- ----------------------------------------------------------------------------
-- Представление расчёта свободного лимита и остатка обязательств (формулы 1.1–1.4)
-- Бизнес-правило 7: Lдост = B - O; поданная заявка Q повторно не вычитается!
-- ----------------------------------------------------------------------------
CREATE OR ALTER VIEW finance.vFreeLimit
AS
SELECT
    p.Id AS ProjectId,
    p.Code AS ProjectCode,
    p.Name AS ProjectName,
    co.Id AS ConstructionObjectId,
    co.Name AS ConstructionObjectName,
    ci.Id AS CostItemId,
    ci.Code AS CostItemCode,
    ci.Name AS CostItemName,
    bv.Id AS BudgetVersionId,
    bv.VersionNumber,
    ISNULL(lim.BudgetAmount, 0) AS ApprovedBudget,
    ISNULL(obl.GrossObligations, 0) AS GrossObligations,
    ISNULL(obl.PaidFact, 0) AS PaidFact,
    ISNULL(req.SubmittedUnpaid, 0) AS SubmittedRequests,
    ISNULL(lim.BudgetAmount, 0) - ISNULL(obl.GrossObligations, 0) AS FreeLimit,
    ISNULL(obl.GrossObligations, 0) - ISNULL(obl.PaidFact, 0) AS ObligationRemainder,
    ISNULL(obl.GrossObligations, 0) - ISNULL(lim.BudgetAmount, 0) AS Deviation,
    CASE
        WHEN ISNULL(lim.BudgetAmount, 0) = 0 THEN NULL
        ELSE CAST(((ISNULL(obl.GrossObligations, 0) - lim.BudgetAmount) / lim.BudgetAmount) * 100.0 AS DECIMAL(10, 2))
    END AS DeviationPercent
FROM finance.Projects p
JOIN finance.BudgetVersions bv ON bv.ProjectId = p.Id AND bv.Status = N'Approved' AND bv.IsDeleted = 0
JOIN finance.ConstructionObjects co ON co.ProjectId = p.Id AND co.IsDeleted = 0
CROSS JOIN finance.CostItems ci
LEFT JOIN (
    SELECT BudgetVersionId, ConstructionObjectId, CostItemId, SUM(Amount) AS BudgetAmount
    FROM finance.BudgetLimits
    WHERE IsDeleted = 0
    GROUP BY BudgetVersionId, ConstructionObjectId, CostItemId
) lim ON lim.BudgetVersionId = bv.Id AND lim.ConstructionObjectId = co.Id AND lim.CostItemId = ci.Id
LEFT JOIN (
    SELECT ConstructionObjectId, CostItemId, SUM(GrossAmount) AS GrossObligations, SUM(PaidAmount) AS PaidFact
    FROM finance.Obligations
    WHERE IsDeleted = 0
    GROUP BY ConstructionObjectId, CostItemId
) obl ON obl.ConstructionObjectId = co.Id AND obl.CostItemId = ci.Id
LEFT JOIN (
    SELECT ConstructionObjectId, CostItemId, SUM(AmountInByn) AS SubmittedUnpaid
    FROM finance.PaymentRequests
    WHERE IsDeleted = 0 AND Status IN (N'UnderReview', N'Blocked', N'PendingApproval', N'ApprovedByManagement', N'Approved')
    GROUP BY ConstructionObjectId, CostItemId
) req ON req.ConstructionObjectId = co.Id AND req.CostItemId = ci.Id
WHERE p.IsDeleted = 0 AND ci.IsDeleted = 0 AND (lim.BudgetAmount IS NOT NULL OR obl.GrossObligations IS NOT NULL);
GO
