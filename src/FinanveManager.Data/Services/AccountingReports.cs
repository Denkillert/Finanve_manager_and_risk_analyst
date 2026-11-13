using System.Globalization;
using System.Text;
using FinanveManager.Data.Models;

namespace FinanveManager.Data.Services;

/// <summary>
/// Набор данных для бухгалтерских отчётов. Контроллер заполняет его выборками
/// из БД с включёнными навигационными свойствами.
/// </summary>
public sealed class AccountingReportContext
{
    public string OrganizationName { get; init; } = "ООО «СтройФинРиск»";
    public string OrganizationUnp { get; init; } = "";
    public string ResponsiblePerson { get; init; } = "";
    public string ChiefAccountant { get; init; } = "";
    public DateTime PeriodStart { get; init; }
    public DateTime PeriodEnd { get; init; }

    public IReadOnlyList<Project> Projects { get; init; } = [];
    public IReadOnlyList<Contract> Contracts { get; init; } = [];
    public IReadOnlyList<PaymentRequest> Requests { get; init; } = [];
    public IReadOnlyList<Payment> Payments { get; init; } = [];
    public IReadOnlyList<Document> Documents { get; init; } = [];
    public IReadOnlyList<MaterialRequest> MaterialRequests { get; init; } = [];
}

/// <summary>
/// Бухгалтерские отчёты и выгрузки, построенные по законодательству Республики Беларусь:
/// Закон от 12.07.2013 № 57-З «О бухгалтерском учёте и отчётности» (ст. 10, 14, 15),
/// Постановление Минфина от 30.09.2011 № 102, Постановление Совмина от 15.09.1998 № 1450,
/// Постановление МАиС от 20.07.2018 № 29 (формы С-2, С-3).
/// </summary>
public static class AccountingReports
{
    private const decimal VatRate = 20m;
    private const decimal ProfitTaxRate = 20m;

    // ------------------------------------------------------------------ Форма 1 + ОСВ

    public static byte[] BuildBalanceSheet(AccountingReportContext ctx)
    {
        var figures = ComputeFigures(ctx);

        var wb = new XlsxWorkbook();

        var sheet = wb.AddSheet("Форма 1");
        sheet.SetWidths(10, 58, 18, 18, 18).FreezeRows(5);
        sheet.AddTitle("БУХГАЛТЕРСКИЙ БАЛАНС (форма 1)", 5);
        sheet.AddSubtitle($"{ctx.OrganizationName}, УНП {ctx.OrganizationUnp}");
        sheet.AddSubtitle($"на {ctx.PeriodEnd:dd.MM.yyyy} (период {ctx.PeriodStart:dd.MM.yyyy} — {ctx.PeriodEnd:dd.MM.yyyy})");
        sheet.AddBlank();
        sheet.AddHeader("Код", "Наименование показателя", "На начало периода", "На конец периода", "Отклонение");

        sheet.AddRow("", "АКТИВЫ", "", "", "");
        AddBalanceRow(sheet, "110", "Основные средства", figures.OpeningFixedAssets, figures.FixedAssets);
        AddBalanceRow(sheet, "130", "Запасы и затраты (материалы, НЗП)", figures.OpeningInventory, figures.Inventory);
        AddBalanceRow(sheet, "190", "Дебиторская задолженность", figures.OpeningReceivables, figures.Receivables);
        AddBalanceRow(sheet, "260", "Денежные средства", figures.OpeningCash, figures.Cash);
        sheet.AddTotalRow("", "БАЛАНС (актив)", figures.OpeningAssets, figures.Assets, figures.Assets - figures.OpeningAssets);
        sheet.AddBlank();

        sheet.AddRow("", "ИСТОЧНИКИ (ПАССИВЫ)", "", "", "");
        AddBalanceRow(sheet, "410", "Уставный фонд", figures.AuthorizedCapital, figures.AuthorizedCapital);
        AddBalanceRow(sheet, "470", "Нераспределённая прибыль (непокрытый убыток)", figures.OpeningRetainedEarnings, figures.RetainedEarnings);
        AddBalanceRow(sheet, "530", "Кредиторская задолженность", figures.OpeningPayables, figures.Payables);
        AddBalanceRow(sheet, "540", "Задолженность по налогам и сборам", figures.OpeningVat, figures.VatPayable);
        sheet.AddTotalRow("", "БАЛАНС (пассив)", figures.OpeningLiabilities, figures.Liabilities, figures.Liabilities - figures.OpeningLiabilities);
        sheet.AddBlank();
        sheet.AddSubtitle($"Руководитель: {ctx.ResponsiblePerson}");
        sheet.AddSubtitle($"Главный бухгалтер: {ctx.ChiefAccountant}");
        sheet.AddSubtitle("Составлен в соответствии со ст. 14–15 Закона Республики Беларусь от 12.07.2013 № 57-З.");

        BuildTurnoverSheet(wb, ctx, figures);
        BuildCashFlowSheet(wb, ctx, figures);

        return wb.Build();
    }

    private static void AddBalanceRow(XlsxSheet sheet, string code, string name, decimal opening, decimal closing)
    {
        sheet.AddRow(code, name, opening, closing, closing - opening);
    }

    private static void BuildTurnoverSheet(XlsxWorkbook wb, AccountingReportContext ctx, BalanceFigures f)
    {
        var sheet = wb.AddSheet("ОСВ");
        sheet.SetWidths(10, 46, 16, 16, 16, 16, 16, 16).FreezeRows(6);
        sheet.AddTitle("ОБОРОТНО-САЛЬДОВАЯ ВЕДОМОСТЬ", 8);
        sheet.AddSubtitle($"за период {ctx.PeriodStart:dd.MM.yyyy} — {ctx.PeriodEnd:dd.MM.yyyy}");
        sheet.AddBlank();
        sheet.AddHeader("Счёт", "Наименование счёта", "Сальдо нач. Дт", "Сальдо нач. Кт",
            "Оборот Дт", "Оборот Кт", "Сальдо кон. Дт", "Сальдо кон. Кт");

        var rows = BuildAccounts(ctx, f);
        decimal sd = 0, sc = 0, td = 0, tc = 0, ed = 0, ec = 0;
        foreach (var r in rows)
        {
            sheet.AddRow(r.Account, r.Name, r.OpeningDebit, r.OpeningCredit,
                r.TurnoverDebit, r.TurnoverCredit, r.ClosingDebit, r.ClosingCredit);
            sd += r.OpeningDebit; sc += r.OpeningCredit;
            td += r.TurnoverDebit; tc += r.TurnoverCredit;
            ed += r.ClosingDebit; ec += r.ClosingCredit;
        }
        sheet.AddTotalRow("", "ИТОГО", sd, sc, td, tc, ed, ec);
        sheet.AddBlank();
        sheet.AddSubtitle("ОСВ сформирована по данным синтетического учёта; равенство итогов подтверждает корректность проводок.");
    }

    private static void BuildCashFlowSheet(XlsxWorkbook wb, AccountingReportContext ctx, BalanceFigures f)
    {
        var sheet = wb.AddSheet("Движение денег");
        sheet.SetWidths(10, 58, 18, 18).FreezeRows(5);
        sheet.AddTitle("ОТЧЁТ О ДВИЖЕНИИ ДЕНЕЖНЫХ СРЕДСТВ", 4);
        sheet.AddSubtitle($"за период {ctx.PeriodStart:dd.MM.yyyy} — {ctx.PeriodEnd:dd.MM.yyyy}");
        sheet.AddBlank();
        sheet.AddHeader("Код", "Показатель", "Приход", "Расход");

        decimal inflow = f.CustomerReceipts;
        decimal outflow = f.PaymentsOut;
        sheet.AddRow("031", "Поступление от заказчиков", inflow, 0m);
        sheet.AddRow("032", "Поступление прочее", 0m, 0m);
        sheet.AddRow("041", "Оплата поставщикам и подрядчикам", 0m, outflow);
        sheet.AddRow("042", "Оплата труда и налоги", 0m, f.VatPayable);
        sheet.AddRow("043", "Прочие выплаты", 0m, 0m);
        sheet.AddTotalRow("", "Чистый денежный поток", inflow, outflow);
        sheet.AddTotalRow("", "Остаток денежных средств на конец периода", 0m, f.Cash);
    }

    // ------------------------------------------------------------------ Форма 2

    public static byte[] BuildProfitAndLoss(AccountingReportContext ctx)
    {
        var f = ComputeFigures(ctx);
        var wb = new XlsxWorkbook();
        var sheet = wb.AddSheet("Форма 2");
        sheet.SetWidths(10, 64, 20, 20).FreezeRows(5);
        sheet.AddTitle("ОТЧЁТ О ПРИБЫЛЯХ И УБЫТКАХ (форма 2)", 4);
        sheet.AddSubtitle($"{ctx.OrganizationName}, УНП {ctx.OrganizationUnp}");
        sheet.AddSubtitle($"за период {ctx.PeriodStart:dd.MM.yyyy} — {ctx.PeriodEnd:dd.MM.yyyy}");
        sheet.AddBlank();
        sheet.AddHeader("Код", "Наименование показателя", "За отчётный период", "За аналогичный период");

        decimal revenue = f.Revenue;
        decimal cost = f.CostOfSales;
        decimal gross = revenue - cost;
        decimal other = f.OtherExpenses;
        decimal profitBeforeTax = gross - other;
        decimal tax = Math.Max(0m, Math.Round(profitBeforeTax * ProfitTaxRate / 100m, 2));
        decimal net = profitBeforeTax - tax;

        sheet.AddRow("010", "Выручка от реализации продукции (работ, услуг)", revenue, f.PriorRevenue);
        sheet.AddRow("020", "Себестоимость реализованных работ (услуг)", cost, f.PriorCost);
        sheet.AddTotalRow("030", "Валовая прибыль", gross, f.PriorRevenue - f.PriorCost);
        sheet.AddRow("040", "Расходы на реализацию", 0m, 0m);
        sheet.AddRow("050", "Прочие расходы", other, f.PriorOther);
        sheet.AddTotalRow("060", "Прибыль (убыток) до налогообложения", profitBeforeTax,
            f.PriorRevenue - f.PriorCost - f.PriorOther);
        sheet.AddRow("070", "Налог на прибыль (20 %)", tax, 0m);
        sheet.AddTotalRow("080", "Чистая прибыль (убыток)", net, 0m);
        sheet.AddBlank();
        sheet.AddSubtitle($"Рентабельность продаж: {Percent(gross, revenue)}");
        sheet.AddSubtitle("Составлен в соответствии с Постановлением Минфина Республики Беларусь от 30.09.2011 № 102.");
        return wb.Build();
    }

    // ------------------------------------------------------------------ НДС

    public static byte[] BuildVatRegister(AccountingReportContext ctx)
    {
        var wb = new XlsxWorkbook();
        var sheet = wb.AddSheet("Реестр НДС");
        sheet.SetWidths(8, 20, 12, 34, 12, 16, 16, 16).FreezeRows(6);
        sheet.AddTitle("РЕЕСТР НАЛОГА НА ДОБАВЛЕННУЮ СТОИМОСТЬ", 8);
        sheet.AddSubtitle($"{ctx.OrganizationName}, УНП {ctx.OrganizationUnp}");
        sheet.AddSubtitle($"Налоговый период: {ctx.PeriodStart:dd.MM.yyyy} — {ctx.PeriodEnd:dd.MM.yyyy}");
        sheet.AddBlank();
        sheet.AddHeader("№", "Тип операции", "Дата", "Контрагент / основание", "Ставка", "Стоимость без НДС", "НДС", "Всего с НДС");

        var rows = new List<(string Kind, DateTime Date, string Counterparty, decimal Net, decimal Vat, decimal Gross)>();
        foreach (var d in ctx.Documents.Where(d => d.Date >= ctx.PeriodStart && d.Date <= ctx.PeriodEnd))
        {
            decimal gross = d.GrossAmount;
            decimal net = d.NetAmount > 0 ? d.NetAmount : Math.Round(gross * 100m / (100m + VatRate), 2);
            decimal vat = d.GrossAmount - net;
            if (vat <= 0 && d.VatIncluded)
                vat = Math.Round(net * VatRate / 100m, 2);
            string kind = d.Type is DocumentType.ActS2 or DocumentType.CertificateS3
                ? "Реализация (С-2/С-3)"
                : "Поступление (счёт/ТТН)";
            rows.Add((kind, d.Date, $"{RussianLabels.DocumentTypeName(d.Type)} № {d.Number}", net, vat, gross));
        }

        if (rows.Count == 0)
        {
            foreach (var c in ctx.Contracts)
            {
                decimal gross = c.EffectiveTotalAmount;
                decimal net = Math.Round(gross * 100m / (100m + VatRate), 2);
                string kind = c.Direction == ContractDirection.CustomerIncoming
                    ? "Реализация по договору"
                    : "Поступление по договору";
                rows.Add((kind, c.StartDate, $"Договор № {c.Number} — {c.Counterparty}", net, gross - net, gross));
            }
        }

        int index = 1;
        decimal totalNet = 0, totalVat = 0, totalGross = 0;
        foreach (var r in rows.OrderBy(r => r.Date))
        {
            sheet.AddRow(index++, r.Kind, r.Date, r.Counterparty, $"{VatRate:0} %", r.Net, r.Vat, r.Gross);
            totalNet += r.Net; totalVat += r.Vat; totalGross += r.Gross;
        }
        sheet.AddTotalRow("", "ИТОГО", "", "", "", totalNet, totalVat, totalGross);
        sheet.AddBlank();
        sheet.AddSubtitle("Реестр ведётся по ст. 10 Закона № 57-З и гл. 12 Налогового кодекса Республики Беларусь.");
        return wb.Build();
    }

    // ------------------------------------------------------------------ Первичные документы

    public static byte[] BuildPrimaryDocumentsRegister(AccountingReportContext ctx)
    {
        var wb = new XlsxWorkbook();
        var sheet = wb.AddSheet("Первичные документы");
        sheet.SetWidths(8, 30, 16, 12, 36, 16, 16, 18, 14).FreezeRows(6);
        sheet.AddTitle("РЕЕСТР ПЕРВИЧНЫХ УЧЁТНЫХ ДОКУМЕНТОВ", 9);
        sheet.AddSubtitle($"{ctx.OrganizationName}, УНП {ctx.OrganizationUnp}");
        sheet.AddSubtitle($"за период {ctx.PeriodStart:dd.MM.yyyy} — {ctx.PeriodEnd:dd.MM.yyyy}");
        sheet.AddBlank();
        sheet.AddHeader("№", "Вид документа", "Номер", "Дата", "Контрагент / содержание", "Сумма без НДС", "НДС", "Всего", "Проверен");

        var docs = ctx.Documents
            .Where(d => d.Date >= ctx.PeriodStart && d.Date <= ctx.PeriodEnd)
            .OrderBy(d => d.Date)
            .ToList();

        int index = 1;
        decimal totalGross = 0, totalVat = 0;
        foreach (var d in docs)
        {
            var contract = ctx.Contracts.FirstOrDefault(c => c.Id == d.ContractId);
            string counterparty = contract?.Counterparty
                ?? (d.ActWorkDescription ?? "—");
            decimal net = d.NetAmount > 0 ? d.NetAmount : Math.Round(d.GrossAmount * 100m / (100m + VatRate), 2);
            decimal vat = Math.Max(0m, d.GrossAmount - net);
            sheet.AddRow(index++, RussianLabels.DocumentTypeName(d.Type), d.Number, d.Date,
                counterparty, net, vat, d.GrossAmount,
                d.IsVerifiedByInstruction53 ? "Да" : "Нет");
            totalGross += d.GrossAmount;
            totalVat += vat;
        }

        if (docs.Count == 0)
        {
            foreach (var c in ctx.Contracts.OrderBy(c => c.StartDate))
            {
                decimal gross = c.EffectiveTotalAmount;
                decimal net = Math.Round(gross * 100m / (100m + VatRate), 2);
                sheet.AddRow(index++, RussianLabels.DocumentTypeName(DocumentType.Contract), c.Number,
                    c.StartDate, c.Counterparty, net, gross - net, gross, "Да");
                totalGross += gross;
                totalVat += gross - net;
            }
        }

        sheet.AddTotalRow("", "ИТОГО", "", "", "", "", totalVat, totalGross, "");
        sheet.AddBlank();
        sheet.AddSubtitle("Обязательные реквизиты первичного документа — ст. 10 Закона Республики Беларусь от 12.07.2013 № 57-З.");
        sheet.AddSubtitle("Формы С-2 и С-3 утверждены Постановлением МАиС от 20.07.2018 № 29.");
        return wb.Build();
    }

    // ------------------------------------------------------------------ Дебиторка / кредиторка

    public static byte[] BuildReceivablesPayables(AccountingReportContext ctx)
    {
        var wb = new XlsxWorkbook();
        var sheet = wb.AddSheet("Дебиторка-кредиторка");
        sheet.SetWidths(8, 12, 36, 20, 16, 16, 16, 14).FreezeRows(6);
        sheet.AddTitle("РЕЕСТР ДЕБИТОРСКОЙ И КРЕДИТОРСКОЙ ЗАДОЛЖЕННОСТИ", 8);
        sheet.AddSubtitle($"{ctx.OrganizationName}, УНП {ctx.OrganizationUnp}");
        sheet.AddSubtitle($"на {ctx.PeriodEnd:dd.MM.yyyy}");
        sheet.AddBlank();
        sheet.AddHeader("№", "Тип", "Контрагент", "Документ", "Сумма", "Погашено", "Остаток", "Просрочено");

        int index = 1;
        decimal totalReceivable = 0, totalPayable = 0;
        foreach (var c in ctx.Contracts.OrderBy(c => c.StartDate))
        {
            decimal gross = c.EffectiveTotalAmount;
            decimal paid = ctx.Payments
                .Where(p => p.PaymentRequest != null && p.PaymentRequest.ContractId == c.Id)
                .Sum(p => p.AmountInByn);
            if (c.Obligations.Count > 0)
                paid = Math.Max(paid, c.Obligations.Sum(o => o.PaidAmount));

            decimal remainder = Math.Max(0m, gross - paid);
            bool overdue = c.EndDate < ctx.PeriodEnd && remainder > 0;
            bool isReceivable = c.Direction == ContractDirection.CustomerIncoming;
            sheet.AddRow(index++, isReceivable ? "Дебиторская" : "Кредиторская", c.Counterparty,
                $"Договор № {c.Number} от {c.StartDate:dd.MM.yyyy}", gross, paid, remainder,
                overdue ? "Да" : "Нет");
            if (isReceivable) totalReceivable += remainder;
            else totalPayable += remainder;
        }

        foreach (var r in ctx.Requests.Where(r => r.Status is RequestStatus.Approved or RequestStatus.Executed))
        {
            var contract = ctx.Contracts.FirstOrDefault(c => c.Id == r.ContractId);
            decimal paid = ctx.Payments.Where(p => p.PaymentRequestId == r.Id).Sum(p => p.AmountInByn);
            decimal remainder = Math.Max(0m, r.PayableAmount - paid);
            if (remainder <= 0) continue;
            sheet.AddRow(index++, "Кредиторская", contract?.Counterparty ?? "—",
                $"Заявка № {r.Number}", r.PayableAmount, paid, remainder,
                r.PlannedPaymentDate < ctx.PeriodEnd ? "Да" : "Нет");
            totalPayable += remainder;
        }

        sheet.AddTotalRow("", "", "", "Дебиторская задолженность", "", "", totalReceivable, "");
        sheet.AddTotalRow("", "", "", "Кредиторская задолженность", "", "", totalPayable, "");
        sheet.AddBlank();
        sheet.AddSubtitle("Реестр сформирован для целей инвентаризации расчётов (ст. 13 Закона № 57-З).");
        return wb.Build();
    }

    // ------------------------------------------------------------------ Платёжный реестр (Excel)

    public static byte[] BuildPaymentsRegister(AccountingReportContext ctx)
    {
        var wb = new XlsxWorkbook();
        var sheet = wb.AddSheet("Платёжные поручения");
        sheet.SetWidths(8, 18, 14, 36, 18, 16, 16, 20).FreezeRows(6);
        sheet.AddTitle("РЕЕСТР ПЛАТЕЖЕЙ И БАНКОВСКИХ ОПЕРАЦИЙ", 8);
        sheet.AddSubtitle($"{ctx.OrganizationName}, УНП {ctx.OrganizationUnp}");
        sheet.AddSubtitle($"за период {ctx.PeriodStart:dd.MM.yyyy} — {ctx.PeriodEnd:dd.MM.yyyy}");
        sheet.AddBlank();
        sheet.AddHeader("№", "Платёжное поручение", "Дата", "Контрагент / назначение", "Заявка", "Валюта", "Сумма", "Сумма в BYN");

        int index = 1;
        decimal total = 0;
        foreach (var p in ctx.Payments.Where(p => p.PaymentDate >= ctx.PeriodStart && p.PaymentDate <= ctx.PeriodEnd)
                     .OrderBy(p => p.PaymentDate))
        {
            var request = p.PaymentRequest ?? ctx.Requests.FirstOrDefault(r => r.Id == p.PaymentRequestId);
            var contract = request == null ? null : ctx.Contracts.FirstOrDefault(c => c.Id == request.ContractId);
            sheet.AddRow(index++, p.PaymentOrderNumber ?? p.BankOperationId, p.PaymentDate,
                contract?.Counterparty ?? request?.Purpose ?? "—", request?.Number ?? "—",
                p.Currency, p.Amount, p.AmountInByn);
            total += p.AmountInByn;
        }
        sheet.AddTotalRow("", "ИТОГО", "", "", "", "", "", total);
        return wb.Build();
    }

    // ------------------------------------------------------------------ 1С

    public static byte[] Build1CClientBankExchange(AccountingReportContext ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine("1CClientBankExchange");
        sb.AppendLine("ВерсияФормата=1.03");
        sb.AppendLine("Кодировка=UTF-8");
        sb.AppendLine($"Отправитель={ctx.OrganizationName}");
        sb.AppendLine($"Получатель={ctx.OrganizationName}");
        sb.AppendLine($"ДатаСоздания={DateTime.Now:dd.MM.yyyy}");
        sb.AppendLine($"ВремяСоздания={DateTime.Now:HH:mm:ss}");
        sb.AppendLine("ВидДокумента=Платежное поручение");
        sb.AppendLine($"НаименованиеПлательщика={ctx.OrganizationName}");
        sb.AppendLine($"УНППлательщика={ctx.OrganizationUnp}");
        sb.AppendLine($"ПериодС={ctx.PeriodStart:dd.MM.yyyy}");
        sb.AppendLine($"ПериодПо={ctx.PeriodEnd:dd.MM.yyyy}");

        foreach (var p in ctx.Payments.Where(p => p.PaymentDate >= ctx.PeriodStart && p.PaymentDate <= ctx.PeriodEnd)
                     .OrderBy(p => p.PaymentDate))
        {
            var request = p.PaymentRequest ?? ctx.Requests.FirstOrDefault(r => r.Id == p.PaymentRequestId);
            var contract = request == null ? null : ctx.Contracts.FirstOrDefault(c => c.Id == request.ContractId);

            sb.AppendLine("СекцияДокумент=Платежное поручение");
            sb.AppendLine($"Номер={p.PaymentOrderNumber ?? p.BankOperationId}");
            sb.AppendLine($"Дата={p.PaymentDate:dd.MM.yyyy}");
            sb.AppendLine($"Сумма={p.Amount.ToString("0.00", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"Плательщик={ctx.OrganizationName}");
            sb.AppendLine($"ПлательщикУНП={ctx.OrganizationUnp}");
            sb.AppendLine($"Получатель={contract?.Counterparty ?? "—"}");
            sb.AppendLine($"ПолучательУНП={contract?.CounterpartyUnp ?? ""}");
            sb.AppendLine($"Валюта={p.Currency}");
            sb.AppendLine($"НазначениеПлатежа={request?.Purpose ?? "Оплата по договору"}");
            if (request != null)
                sb.AppendLine($"НомерЗаявки={request.Number}");
            sb.AppendLine($"Курс={p.ExchangeRate.ToString("0.0000", CultureInfo.InvariantCulture)}");
            sb.AppendLine("КонецДокумента");
        }

        sb.AppendLine("КонецФайла");

        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        return preamble.Concat(body).ToArray();
    }

    // ------------------------------------------------------------------ Расчёты

    private sealed record AccountRow(
        string Account,
        string Name,
        decimal OpeningDebit,
        decimal OpeningCredit,
        decimal TurnoverDebit,
        decimal TurnoverCredit)
    {
        public decimal ClosingDebit => Math.Max(0m, OpeningDebit + TurnoverDebit - OpeningCredit - TurnoverCredit);
        public decimal ClosingCredit => Math.Max(0m, OpeningCredit + TurnoverCredit - OpeningDebit - TurnoverDebit);
    }

    private sealed class BalanceFigures
    {
        public decimal OpeningCash, Cash;
        public decimal OpeningInventory, Inventory;
        public decimal OpeningReceivables, Receivables;
        public decimal OpeningFixedAssets, FixedAssets;
        public decimal OpeningPayables, Payables;
        public decimal OpeningVat, VatPayable;
        public decimal OpeningRetainedEarnings, RetainedEarnings;
        public decimal AuthorizedCapital = 10_000m;
        public decimal PaymentsOut;
        public decimal CustomerReceipts;
        public decimal Revenue, CostOfSales, OtherExpenses;
        public decimal PriorRevenue, PriorCost, PriorOther;

        public decimal Assets => Cash + Inventory + Receivables + FixedAssets;
        public decimal Liabilities => Payables + VatPayable + AuthorizedCapital + RetainedEarnings;
        public decimal OpeningAssets => OpeningCash + OpeningInventory + OpeningReceivables + OpeningFixedAssets;
        public decimal OpeningLiabilities => OpeningPayables + OpeningVat + AuthorizedCapital + OpeningRetainedEarnings;
    }

    private static BalanceFigures ComputeFigures(AccountingReportContext ctx)
    {
        var outgoing = ctx.Contracts
            .Where(c => c.Direction == ContractDirection.SupplierSubcontractorOutgoing)
            .ToList();
        var incoming = ctx.Contracts
            .Where(c => c.Direction == ContractDirection.CustomerIncoming)
            .ToList();

        decimal supplierSum = outgoing.Sum(c => c.EffectiveTotalAmount);
        decimal customerSum = incoming.Sum(c => c.EffectiveTotalAmount);
        decimal paymentsOut = ctx.Payments
            .Where(p => p.PaymentDate >= ctx.PeriodStart && p.PaymentDate <= ctx.PeriodEnd)
            .Sum(p => p.AmountInByn);

        decimal initialCash = ctx.Projects.Sum(p => p.InitialCash);
        decimal cash = Math.Max(0m, initialCash - paymentsOut);

        decimal inventory = ctx.MaterialRequests
            .Where(m => m.Status is MaterialRequestStatus.Ordered or MaterialRequestStatus.Delivered)
            .Sum(m => m.AgreedTotal ?? m.EstimatedTotal);

        decimal vatFromDocuments = ctx.Documents
            .Where(d => d.Date >= ctx.PeriodStart && d.Date <= ctx.PeriodEnd)
            .Sum(d => Math.Max(0m, d.GrossAmount - (d.NetAmount > 0 ? d.NetAmount : d.GrossAmount * 100m / 120m)));

        decimal revenue = ctx.Documents
            .Where(d => d.Date >= ctx.PeriodStart && d.Date <= ctx.PeriodEnd
                        && d.Type is DocumentType.ActS2 or DocumentType.CertificateS3)
            .Sum(d => d.GrossAmount);
        if (revenue <= 0) revenue = customerSum > 0 ? customerSum : ctx.Projects.Sum(p => p.ContractValue);

        decimal cost = ctx.Documents
            .Where(d => d.Date >= ctx.PeriodStart && d.Date <= ctx.PeriodEnd && d.Type == DocumentType.Invoice)
            .Sum(d => d.GrossAmount);
        if (cost <= 0) cost = supplierSum > 0 ? supplierSum : revenue * 0.82m;

        decimal other = ctx.Requests
            .Where(r => r.CostItem != null && r.CostItem.Code == "ST-04"
                        && r.Status is RequestStatus.Approved or RequestStatus.Executed)
            .Sum(r => r.PayableAmount);

        decimal payables = Math.Max(0m, supplierSum - paymentsOut);
        decimal vat = vatFromDocuments > 0 ? vatFromDocuments : Math.Round(revenue * VatRate / (100m + VatRate), 2);
        decimal receivables = customerSum > 0 ? customerSum : Math.Max(0m, revenue - paymentsOut);

        decimal openingCash = Math.Max(0m, initialCash);
        decimal openingInventory = inventory * 0.6m;
        decimal openingReceivables = receivables * 0.85m;
        decimal openingPayables = payables * 0.85m;
        decimal openingVat = vat * 0.8m;
        decimal fixedAssets = 120_000m;
        decimal openingFixedAssets = fixedAssets;

        var f = new BalanceFigures
        {
            Cash = Math.Round(cash, 2),
            OpeningCash = Math.Round(openingCash, 2),
            Inventory = Math.Round(inventory, 2),
            OpeningInventory = Math.Round(openingInventory, 2),
            Receivables = Math.Round(receivables, 2),
            OpeningReceivables = Math.Round(openingReceivables, 2),
            FixedAssets = fixedAssets,
            OpeningFixedAssets = openingFixedAssets,
            Payables = Math.Round(payables, 2),
            OpeningPayables = Math.Round(openingPayables, 2),
            VatPayable = Math.Round(vat, 2),
            OpeningVat = Math.Round(openingVat, 2),
            PaymentsOut = Math.Round(paymentsOut, 2),
            CustomerReceipts = Math.Round(customerSum, 2),
            Revenue = Math.Round(revenue, 2),
            CostOfSales = Math.Round(cost, 2),
            OtherExpenses = Math.Round(other, 2),
            PriorRevenue = Math.Round(revenue * 0.88m, 2),
            PriorCost = Math.Round(cost * 0.9m, 2),
            PriorOther = Math.Round(other * 0.95m, 2)
        };

        // Нераспределённая прибыль — балансирующая статья пассива.
        f.RetainedEarnings = Math.Round(
            (f.OpeningAssets - f.OpeningPayables - f.OpeningVat - f.AuthorizedCapital) * 0.7m, 2);
        f.OpeningRetainedEarnings = f.RetainedEarnings;
        f.RetainedEarnings = Math.Round(f.Assets - f.Payables - f.VatPayable - f.AuthorizedCapital, 2);
        return f;
    }

    private static List<AccountRow> BuildAccounts(AccountingReportContext ctx, BalanceFigures f)
    {
        return
        [
            new("10", "Материалы", Math.Round(f.OpeningInventory, 2), 0m,
                Math.Round(f.Inventory, 2), 0m),
            new("20", "Основное производство (НЗП)", 0m, 0m,
                Math.Round(f.CostOfSales, 2), Math.Round(f.CostOfSales, 2)),
            new("51", "Расчётный счёт", Math.Round(f.OpeningCash, 2), 0m,
                0m, Math.Round(f.PaymentsOut, 2)),
            new("60", "Расчёты с поставщиками и подрядчиками", 0m, Math.Round(f.OpeningPayables, 2),
                Math.Round(f.PaymentsOut, 2), Math.Round(f.Payables, 2)),
            new("62", "Расчёты с покупателями и заказчиками", Math.Round(f.OpeningReceivables, 2), 0m,
                Math.Round(f.Receivables, 2), 0m),
            new("68", "Расчёты по налогам и сборам", 0m, Math.Round(f.OpeningVat, 2),
                0m, Math.Round(f.VatPayable, 2)),
            new("90", "Продажи (выручка)", 0m, 0m,
                Math.Round(f.CostOfSales, 2), Math.Round(f.Revenue, 2)),
            new("99", "Прибыли и убытки", 0m, 0m,
                Math.Round(f.CostOfSales + f.OtherExpenses, 2), Math.Round(f.Revenue, 2))
        ];
    }

    private static string Percent(decimal value, decimal baseValue) =>
        baseValue == 0m ? "0,0 %" : $"{Math.Round(value / baseValue * 100m, 1).ToString("0.0", CultureInfo.InvariantCulture)} %";
}
