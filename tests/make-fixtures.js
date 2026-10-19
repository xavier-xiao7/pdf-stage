const fs = require('fs');
const path = require('path');
const base = path.resolve(process.argv[2] || __dirname);
fs.mkdirSync(base, { recursive: true });
function makePdf(file, pages) {
  const objs = [null, '<< /Type /Catalog /Pages 2 0 R >>', '', '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>'];
  const kids = [];
  for (const p of pages) {
    const pageId = objs.length;
    kids.push(`${pageId} 0 R`);
    objs.push(`<< /Type /Page /Parent 2 0 R /MediaBox [0 0 ${p.w} ${p.h}] /Resources << /Font << /F1 3 0 R >> >> /Contents ${pageId + 1} 0 R >>`);
    const body = p.content;
    objs.push(`<< /Length ${Buffer.byteLength(body, 'ascii')} >>\nstream\n${body}\nendstream`);
  }
  objs[2] = `<< /Type /Pages /Count ${kids.length} /Kids [${kids.join(' ')}] >>`;
  let pdf = '%PDF-1.7\n', offsets = [0];
  for (let i = 1; i < objs.length; i++) { offsets[i] = Buffer.byteLength(pdf, 'ascii'); pdf += `${i} 0 obj\n${objs[i]}\nendobj\n`; }
  const xref = Buffer.byteLength(pdf, 'ascii');
  pdf += `xref\n0 ${objs.length}\n0000000000 65535 f \n`;
  for (const off of offsets.slice(1)) pdf += `${String(off).padStart(10, '0')} 00000 n \n`;
  pdf += `trailer\n<< /Size ${objs.length} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
  fs.writeFileSync(file, pdf, 'ascii');
}
function text(x, y, size, content, color = '1 1 1') { return `${color} rg BT /F1 ${size} Tf ${x} ${y} Td (${content}) Tj ET`; }
const bg = '0.063 0.078 0.114 rg 0 0 960 540 re f\n0.392 0.839 0.725 rg 64 442 56 4 re f\n';
const demo = [
  { w:960, h:540, content: bg + [text(64, 470, 13, 'PDF STAGE     /     WINDOWED PRESENTATION', '0.65 0.72 0.8'), text(60, 334, 60, 'Your slides.'), text(60, 262, 60, 'Your space.'), text(64, 195, 19, 'A clean presentation window. Room for your notes.', '0.65 0.72 0.8'), text(64, 62, 14, 'CLICK OR PRESS RIGHT TO CONTINUE', '0.392 0.839 0.725'), text(848, 62, 14, '01 / 03', '0.65 0.72 0.8')].join('\n') },
  { w:960, h:540, content:bg + [text(64, 470, 13, 'PDF STAGE     /     SIMPLE CONTROLS', '0.65 0.72 0.8'), text(60, 345, 48, 'Present at your pace.'), text(64, 265, 24, 'Arrow keys / Click / Wheel     -     Turn pages'), text(64, 214, 24, 'Drag anywhere                    -     Move window'), text(64, 163, 24, 'Drag an edge                       -     Resize window'), text(64, 62, 14, 'RIGHT-CLICK FOR MORE OPTIONS', '0.392 0.839 0.725'), text(848, 62, 14, '02 / 03', '0.65 0.72 0.8')].join('\n') },
  { w:960, h:540, content:bg + [text(64, 470, 13, 'PDF STAGE     /     READY FOR YOUR MEETING', '0.65 0.72 0.8'), text(60, 340, 48, 'Share this window.'), text(64, 257, 23, 'In Zoom, select the PDF Stage application window.'), text(64, 207, 23, 'Keep your notes beside it.'), text(64, 155, 18, 'Ctrl + O to open your own PDF.', '0.392 0.839 0.725'), text(64, 62, 14, 'PRESS HOME TO START AGAIN', '0.392 0.839 0.725'), text(848, 62, 14, '03 / 03', '0.65 0.72 0.8')].join('\n') }
];
makePdf(path.join(base, 'demo.pdf'), demo);
makePdf(path.join(base, '中文 路径 test.pdf'), demo);
makePdf(path.join(base, 'mixed.pdf'), [demo[0], {w:540, h:960, content:'1 1 1 rg 0 0 540 960 re f\n' + text(50,800,30,'Portrait page', '0 0 0')}, {w:600,h:600,content:'1 1 1 rg 0 0 600 600 re f\n' + text(50,400,30,'Square page','0 0 0')}]);
makePdf(path.join(base, 'large.pdf'), Array.from({length:200}, (_,i)=>({w:960,h:540,content:bg+text(64,270,60,'Page '+(i+1))})));
makePdf(path.join(base, 'empty.pdf'), []);
fs.writeFileSync(path.join(base, 'invalid.pdf'), 'not a PDF');
console.log('Generated demo and 5 test fixtures.');
