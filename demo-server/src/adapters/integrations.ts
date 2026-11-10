import ExcelJS from 'exceljs';
import PDFDocument from 'pdfkit';
import fs from 'node:fs';

export interface ParsedBankOperation {
  bankOperationId: string;
  operationDate: string;
  amount: number;
  currency: string;
  counterpartyName: string;
  paymentRequestNumber: string | null;
  purpose: string;
  paymentOrderNumber: string | null;
}

export function parseBankStatement(content: string, fileName: string): ParsedBankOperation[] {
  if (!content.trim()) return [];
  if (fileName.toLowerCase().endsWith('.mt940') || content.includes(':61:')) {
    return parseMt940(content);
  }
  return parseBankCsv(content);
}

function parseBankCsv(content: string): ParsedBankOperation[] {
  const lines = content.split(/\r?\n/).filter((l) => l.trim().length > 0);
  const result: ParsedBankOperation[] = [];
  for (const line of lines.slice(1)) {
    const sep = line.includes(';') ? ';' : ',';
    const cols = line.split(sep).map((c) => c.trim());
    if (cols.length < 3) continue;
    const amount = Number(cols[2].replace(/\s+/g, '').replace(',', '.'));
    if (Number.isNaN(amount) || amount <= 0) continue;
    result.push({
      bankOperationId: cols[0],
      operationDate: /^\d{4}-\d{2}-\d{2}/.test(cols[1]) ? cols[1].slice(0, 10) : new Date().toISOString().slice(0, 10),
      amount,
      currency: (cols[3] || 'BYN').toUpperCase(),
      counterpartyName: cols[4] || '',
      paymentRequestNumber: cols[5] || null,
      purpose: cols[6] || '',
      paymentOrderNumber: cols[7] || null,
    });
  }
  return result;
}

function parseMt940(content: string): ParsedBankOperation[] {
  const result: ParsedBankOperation[] = [];
  const regex = /:61:(\d{6})([CD])([\d,.]+)N\w+\/\/([^\r\n]+)(?:\r?\n:86:([^\r\n:]+))?/g;
  let match: RegExpExecArray | null;
  while ((match = regex.exec(content)) !== null) {
    const yy = match[1].slice(0, 2);
    const mm = match[1].slice(2, 4);
    const dd = match[1].slice(4, 6);
    const dateStr = `20${yy}-${mm}-${dd}`;
    const amount = Number(match[3].replace(',', '.'));
    const opId = match[4].trim();
    const purpose = (match[5] || '').trim();
    const reqMatch = purpose.match(/(ЗП-\d+-[A-Z0-9]+)/i);
    if (!Number.isNaN(amount) && amount > 0) {
      result.push({
        bankOperationId: opId,
        operationDate: dateStr,
        amount,
        currency: 'BYN',
        counterpartyName: '',
        paymentRequestNumber: reqMatch ? reqMatch[1].toUpperCase() : null,
        purpose,
        paymentOrderNumber: opId,
      });
    }
  }
  return result;
}

export interface EstimateLine {
  costItemCode: string;
  description: string;
  amount: number;
  matchedCostItemId: string | null;
  matchedCostItemName: string | null;
  matched: boolean;
}

export function importEstimateContent(
  content: string,
  fileName: string,
  catalog: Array<{ id: string; code: string; name: string; category: string }>
) {
  const lines: EstimateLine[] = [];
  const trimmed = content.trim();

  if (fileName.toLowerCase().endsWith('.xml') || trimmed.startsWith('<')) {
    const itemRegex = /<Item\b([^>]*)>([\s\S]*?)<\/Item>|<Item\b([^/>]*)\/>/gi;
    let m: RegExpExecArray | null;
    while ((m = itemRegex.exec(trimmed)) !== null) {
      const attrs = (m[1] || m[3] || '');
      const body = m[2] || '';
      const code = extractXmlAttrOrTag(attrs, body, 'Code');
      const desc = extractXmlAttrOrTag(attrs, body, 'Name');
      const amtStr = extractXmlAttrOrTag(attrs, body, 'Amount');
      const amount = Number((amtStr || '0').replace(',', '.')) || 0;
      const matchItem = findCatalogMatch(code, desc, catalog);
      lines.push({
        costItemCode: code,
        description: desc,
        amount,
        matchedCostItemId: matchItem?.id ?? null,
        matchedCostItemName: matchItem?.name ?? null,
        matched: Boolean(matchItem),
      });
    }
  } else {
    const rows = trimmed.split(/\r?\n/).filter((r) => r.trim().length > 0);
    for (const row of rows.slice(1)) {
      const cols = row.split(row.includes(';') ? ';' : ',').map((c) => c.trim());
      if (cols.length < 3) continue;
      const code = cols[0];
      const desc = cols[1];
      const amount = Number(cols[2].replace(/\s+/g, '').replace(',', '.')) || 0;
      const matchItem = findCatalogMatch(code, desc, catalog);
      lines.push({
        costItemCode: code,
        description: desc,
        amount,
        matchedCostItemId: matchItem?.id ?? null,
        matchedCostItemName: matchItem?.name ?? null,
        matched: Boolean(matchItem),
      });
    }
  }

  const matchedLines = lines.filter((l) => l.matched).length;
  const matchPercent = lines.length === 0 ? 0 : Math.round((matchedLines / lines.length) * 1000) / 10;
  return { totalLines: lines.length, matchedLines, matchPercent, lines };
}

function extractXmlAttrOrTag(attrs: string, body: string, name: string): string {
  const attrMatch = attrs.match(new RegExp(`${name}\\s*=\\s*["']([^"']*)["']`, 'i'));
  if (attrMatch) return attrMatch[1];
  const tagMatch = body.match(new RegExp(`<${name}>([\\s\\S]*?)<\\/${name}>`, 'i'));
  return tagMatch ? tagMatch[1].trim() : '';
}

function findCatalogMatch(
  code: string,
  desc: string,
  catalog: Array<{ id: string; code: string; name: string; category: string }>
) {
  const lcCode = code.toLowerCase();
  const lcDesc = desc.toLowerCase();
  return catalog.find(
    (c) =>
      c.code.toLowerCase() === lcCode ||
      lcDesc.includes(c.category.toLowerCase()) ||
      (lcDesc.includes('бетон') && c.code === 'ST-01') ||
      (lcDesc.includes('арматур') && c.code === 'ST-01') ||
      (lcDesc.includes('монтаж') && c.code === 'ST-02') ||
      (lcDesc.includes('субподряд') && c.code === 'ST-02') ||
      (lcDesc.includes('оборудов') && c.code === 'ST-03') ||
      (lcDesc.includes('транспорт') && c.code === 'ST-04') ||
      (lcDesc.includes('наклад') && c.code === 'ST-04')
  );
}

export interface TabularReportModel {
  title: string;
  subtitle: string;
  columns: string[];
  rows: Array<Array<string | number>>;
  summaryLines: string[];
}

export async function generateExcelReportBuffer(model: TabularReportModel): Promise<Buffer> {
  const wb = new ExcelJS.Workbook();
  wb.creator = 'СтройФинРиск';
  wb.created = new Date();
  const ws = wb.addWorksheet('Отчёт');

  ws.addRow([model.title]);
  ws.getRow(1).font = { bold: true, size: 14 };
  ws.addRow([model.subtitle]);
  ws.addRow([]);

  const headerRow = ws.addRow(model.columns);
  headerRow.font = { bold: true, color: { argb: 'FFFFFFFF' } };
  headerRow.eachCell((cell) => {
    cell.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF1E3A5F' } };
  });

  for (const r of model.rows) {
    ws.addRow(r);
  }

  ws.addRow([]);
  for (const s of model.summaryLines) {
    const sr = ws.addRow([s]);
    sr.font = { bold: true };
  }

  ws.columns.forEach((col) => {
    col.width = 24;
  });

  const arrayBuffer = await wb.xlsx.writeBuffer();
  return Buffer.from(arrayBuffer);
}

export async function generatePdfReportBuffer(model: TabularReportModel): Promise<Buffer> {
  return new Promise((resolve, reject) => {
    const doc = new PDFDocument({ margin: 36, size: 'A4', layout: 'landscape' });
    const chunks: Buffer[] = [];
    doc.on('data', (c) => chunks.push(c));
    doc.on('end', () => resolve(Buffer.concat(chunks)));
    doc.on('error', reject);

    const fontRegular = '/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf';
    const fontBold = '/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf';
    if (fs.existsSync(fontRegular)) doc.registerFont('Regular', fontRegular);
    if (fs.existsSync(fontBold)) doc.registerFont('Bold', fontBold);

    const useBold = fs.existsSync(fontBold) ? 'Bold' : 'Helvetica-Bold';
    const useReg = fs.existsSync(fontRegular) ? 'Regular' : 'Helvetica';

    doc.font(useBold).fontSize(15).fillColor('#12263F').text(model.title);
    doc.moveDown(0.3);
    doc.font(useReg).fontSize(9).fillColor('#4A5568').text(model.subtitle);
    doc.moveDown(0.8);

    const colWidth = Math.floor((doc.page.width - 72) / Math.max(1, model.columns.length));
    let y = doc.y;

    // Header
    doc.rect(36, y, doc.page.width - 72, 22).fill('#1E3A5F');
    doc.fillColor('#FFFFFF').font(useBold).fontSize(8.5);
    model.columns.forEach((col, i) => {
      doc.text(String(col), 40 + i * colWidth, y + 6, { width: colWidth - 8, ellipsis: true, lineBreak: false });
    });
    y += 24;

    doc.font(useReg).fontSize(8.5);
    for (const row of model.rows.slice(0, 32)) {
      if (y > doc.page.height - 90) {
        doc.addPage();
        y = 36;
      }
      doc.fillColor('#1A202C');
      row.forEach((cell, i) => {
        doc.text(String(cell ?? ''), 40 + i * colWidth, y + 4, { width: colWidth - 8, ellipsis: true, lineBreak: false });
      });
      doc.moveTo(36, y + 18).lineTo(doc.page.width - 36, y + 18).strokeColor('#E2E8F0').lineWidth(0.5).stroke();
      y += 19;
    }

    y += 10;
    if (y > doc.page.height - 80) {
      doc.addPage();
      y = 36;
    }
    doc.font(useBold).fontSize(9.5).fillColor('#12263F');
    for (const line of model.summaryLines) {
      doc.text(line, 36, y);
      y += 15;
    }

    doc.end();
  });
}
