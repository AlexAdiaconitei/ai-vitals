import fs from "node:fs";
import path from "node:path";

const [sourcePath, outputPath] = process.argv.slice(2);
if (!sourcePath || !outputPath) {
  throw new Error("Usage: node recover-round.mjs <source-html> <output-json>");
}

const html = fs.readFileSync(sourcePath, "utf8");
const markerStart = '<script id="planning-data" type="application/json">';
const start = html.indexOf(markerStart);
const end = html.indexOf("</script>", start);
if (start < 0 || end < 0) throw new Error("planning-data not found");

const data = JSON.parse(html.slice(start + markerStart.length, end));
const round = {
  ...data.round,
  number: 3,
  slug: "plan-final-validado",
  title: "Plan final validado",
  date: "2026-08-05",
};
delete round.file;

const payload = {
  schemaVersion: 1,
  outputDir: data.outputDir ?? path.dirname(sourcePath),
  dossier: data.dossier,
  round,
  currentState: data.manifest?.current ?? data.currentState,
  ui: data.ui,
};

fs.writeFileSync(outputPath, `${JSON.stringify(payload, null, 2)}\n`, "utf8");
console.log(JSON.stringify({ sourcePath, outputPath, keys: Object.keys(data) }));
