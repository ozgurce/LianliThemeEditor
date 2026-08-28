import csv
import json
import os
import re
import subprocess
import sys
from collections import Counter, defaultdict
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
REPORT_DIR = REPO / "reports"
REPORT_DIR.mkdir(exist_ok=True)

SUPPORTER_CANDIDATES = [
    REPO / "_build_check" / "UsbMonitorL.exe",
    REPO / "SupporterCs" / "bin" / "Debug" / "net48" / "UsbMonitorL.exe",
]
SUPPORTER = next((p for p in SUPPORTER_CANDIDATES if p.exists()), None)
if SUPPORTER is None:
    raise SystemExit("UsbMonitorL.exe was not found. Build the supporter first.")

PROGRAMDATA_ROOT = Path(r"C:\ProgramData\Lian-Li\L-Connect 3")
OLED_CURVE_ROOT = Path(r"C:\Users\Ozgur\Desktop\hydroshift-ii-oled-curve")
DECOMPILE_ROOT = Path(r"C:\Users\Ozgur\Documents\lconnect decompile")

SKIP_DIRS = {
    "cefsharp",
    "logs",
    "temp",
}

IGNORED_MISSING_SOURCES = {"VOLUME", "RAMMODEL"}

EDITOR_MAIN = (REPO / "MainWindow.xaml.cs").read_text(encoding="utf-8-sig", errors="ignore")


def parse_editor_data_sources():
    match = re.search(r"private\s+static\s+readonly\s+string\[\]\s+DataSources\s*=\s*\{(?P<body>.*?)\};", EDITOR_MAIN, re.S)
    sources = set(re.findall(r'"([^"]+)"', match.group("body") if match else ""))
    sources_upper = {s.upper() for s in sources}
    sources_upper.update({f"CASEFAN{i}" for i in range(1, 9)})
    sources_upper.update({"CPUPOWER", "GPUPOWER", "DOWNSPEED", "FPS"})
    return sources_upper


def parse_editor_formats(array_name):
    match = re.search(rf"private\s+static\s+readonly\s+\([^)]*\)\[\]\s+{array_name}\s*=\s*\{{(?P<body>.*?)\}};", EDITOR_MAIN, re.S)
    if not match:
        return []
    return re.findall(r'\("([^"]+)",\s*"[^"]*"\)', match.group("body"))


EDITOR_SOURCES = parse_editor_data_sources()
TIME_FORMATS = set(parse_editor_formats("TimeFormats"))
DATE_FORMATS = set(parse_editor_formats("DateFormats"))
DAY_FORMATS = {"Day_en"}
POWER_FORMATS = {"0.0"}


def norm_source(value):
    key = (value or "").strip().upper()
    if key == "CPUPOWER":
        return "CPUPWR"
    if key == "GPUPOWER":
        return "GPUPWR"
    if key == "DOWNSPEED":
        return "DOWNDSPEED"
    if key == "FPS":
        return "FPS_AVG"
    if key == "STATICTEXT":
        return "STATICTEXT"
    return key


def norm_time_format(value):
    fmt = (value or "").strip()
    if fmt in {"00:00", "HH:mm", "Hour:Minute"}:
        return "h:m"
    if fmt in {"00:00:00", "HH:MM:SS", "H:M:S", "HH:mm:ss", "Hour:Minute:Second"}:
        return "h:m:s"
    return fmt


def editor_format_status(source, fmt):
    source = norm_source(source)
    fmt = norm_time_format(fmt)
    if not fmt:
        return "empty"
    if source == "TIME":
        return "selectable" if fmt in TIME_FORMATS else "not_selectable"
    if source == "DATE":
        return "selectable" if fmt in DATE_FORMATS else "not_selectable"
    if source == "DAY":
        return "selectable" if fmt in DAY_FORMATS else "not_selectable"
    if source in {"CPUPWR", "GPUPWR"}:
        return "selectable" if fmt in POWER_FORMATS else "not_selectable"
    if source in {"HDDTEMP", "HDDUSED"}:
        return "source_supports_format_but_no_values"
    return "format_not_exposed_for_source"


def device_for_path(path):
    text = str(path).lower()
    for name in [
        "flex-lcd",
        "universal-screen-8.8-inch",
        "vm-9.2-inch",
        "hydroshift-ii-lcd-s",
        "hydroshift-ii-lcd-c",
        "hydroshift-ii-oled-curve",
        "lancool207",
    ]:
        if name in text:
            return name
    return "hydroshift-ii-lcd-s"


def source_label(path):
    try:
        rel = path.relative_to(PROGRAMDATA_ROOT)
        return rel.parts[0] if rel.parts else "programdata"
    except ValueError:
        pass
    try:
        path.relative_to(OLED_CURVE_ROOT)
        return "hydroshift-ii-oled-curve-desktop"
    except ValueError:
        return "other"


def iter_assets():
    roots = []
    if PROGRAMDATA_ROOT.exists():
        roots.append(PROGRAMDATA_ROOT)
    if OLED_CURVE_ROOT.exists():
        roots.append(OLED_CURVE_ROOT)
    seen = set()
    for root in roots:
        for current, dirs, files in os.walk(root):
            dirs[:] = [d for d in dirs if d.lower() not in SKIP_DIRS]
            for file_name in files:
                if not file_name.lower().endswith((".template", ".modular")):
                    continue
                path = Path(current) / file_name
                key = str(path).lower()
                if key in seen:
                    continue
                seen.add(key)
                yield path


def inspect_asset(path):
    cmd = [
        str(SUPPORTER),
        "-DeviceModel",
        device_for_path(path),
        "-TemplatePath",
        str(path),
        "-ListLayers",
        "-Json",
    ]
    proc = subprocess.run(cmd, cwd=str(REPO), text=True, encoding="utf-8", errors="replace", capture_output=True, timeout=60)
    if proc.returncode != 0:
        raise RuntimeError((proc.stderr or proc.stdout).strip())
    return json.loads(proc.stdout)


def compact_layer(layer):
    keys = [
        "Index",
        "Type",
        "TypeName",
        "SubTypeName",
        "GraphStyle",
        "SensorStyle",
        "SensorType",
        "DataSource",
        "Format",
        "Media",
        "Width",
        "Height",
        "X",
        "Y",
        "MinValue",
        "MaxValue",
        "WritableProperties",
    ]
    return {k: layer.get(k) for k in keys if k in layer}


def decompile_hits():
    if not DECOMPILE_ROOT.exists():
        return []
    patterns = [
        "AcceptDataList",
        "DataSource",
        "ThemeData",
        "CPUTEMP",
        "RAMMODEL",
        "VOLUME",
        "Day_en",
        "h:m",
        "Y-M-D",
    ]
    hits = []
    for pat in patterns:
        try:
            proc = subprocess.run(
                ["rg", "-n", "-m", "20", pat, str(DECOMPILE_ROOT)],
                text=True,
                encoding="utf-8",
                errors="replace",
                capture_output=True,
                timeout=20,
            )
        except Exception as exc:
            hits.append(f"## {pat}\nERROR: {exc}")
            continue
        if proc.stdout.strip():
            hits.append(f"## {pat}\n{proc.stdout.strip()}")
    return hits


def main():
    assets = list(iter_assets())
    inventory = []
    failures = []
    for index, path in enumerate(assets, 1):
        print(f"[{index}/{len(assets)}] {path}", flush=True)
        try:
            data = inspect_asset(path)
        except Exception as exc:
            failures.append({"Path": str(path), "Source": source_label(path), "Error": str(exc)})
            continue
        layers = [compact_layer(layer) for layer in data.get("Layers", [])]
        inventory.append(
            {
                "Path": str(path),
                "File": path.name,
                "Extension": path.suffix.lower(),
                "Source": source_label(path),
                "DeviceModel": device_for_path(path),
                "ThemeType": data.get("ThemeType"),
                "Width": data.get("Width"),
                "Height": data.get("Height"),
                "Background": data.get("Background"),
                "LayerCount": len(layers),
                "Layers": layers,
            }
        )

    type_counts = Counter()
    type_variant_counts = Counter()
    source_counts = Counter()
    source_examples = defaultdict(list)
    format_counts = Counter()
    format_examples = defaultdict(list)
    sensor_type_counts = Counter()
    graph_style_counts = Counter()
    writable_by_type = defaultdict(Counter)
    rows = []

    for asset in inventory:
        for layer in asset["Layers"]:
            typ = layer.get("Type") or ""
            type_name = layer.get("TypeName") or ""
            sub_type = layer.get("SubTypeName") or ""
            graph_style = layer.get("GraphStyle") or ""
            sensor_type = layer.get("SensorType") or ""
            raw_source = layer.get("DataSource") or ""
            source = norm_source(raw_source)
            fmt = layer.get("Format") or ""
            type_counts[typ] += 1
            type_variant_counts[(typ, type_name, sub_type, graph_style)] += 1
            if source:
                source_counts[source] += 1
                if len(source_examples[source]) < 5:
                    source_examples[source].append(asset["Path"])
            if fmt:
                status = editor_format_status(source, fmt)
                format_counts[(source, norm_time_format(fmt), status)] += 1
                if len(format_examples[(source, norm_time_format(fmt), status)]) < 5:
                    format_examples[(source, norm_time_format(fmt), status)].append(asset["Path"])
            if sensor_type:
                sensor_type_counts[sensor_type] += 1
            if graph_style:
                graph_style_counts[graph_style] += 1
            for prop in layer.get("WritableProperties") or []:
                writable_by_type[typ][prop] += 1
            rows.append(
                {
                    "source": asset["Source"],
                    "device": asset["DeviceModel"],
                    "file": asset["File"],
                    "path": asset["Path"],
                    "layer_type": typ,
                    "type_name": type_name,
                    "sub_type": sub_type,
                    "graph_style": graph_style,
                    "data_source": source,
                    "format": norm_time_format(fmt),
                    "format_status": editor_format_status(source, fmt),
                    "sensor_type": sensor_type,
                }
            )

    missing_sources = []
    for source, count in sorted(source_counts.items()):
        if source in {"", "STATICTEXT"} or source in IGNORED_MISSING_SOURCES:
            continue
        if source not in {norm_source(s) for s in EDITOR_SOURCES}:
            missing_sources.append({"DataSource": source, "Count": count, "Examples": source_examples[source]})

    report = {
        "supporter": str(SUPPORTER),
        "assetCount": len(assets),
        "loadedCount": len(inventory),
        "failureCount": len(failures),
        "sourcesScanned": sorted(set(source_label(p) for p in assets)),
        "editorDataSources": sorted(EDITOR_SOURCES),
        "editorFormats": {
            "TIME": sorted(TIME_FORMATS),
            "DATE": sorted(DATE_FORMATS),
            "DAY": sorted(DAY_FORMATS),
            "CPUPWR_GPUPWR": sorted(POWER_FORMATS),
        },
        "typeCounts": dict(type_counts.most_common()),
        "typeVariants": [
            {"Type": k[0], "TypeName": k[1], "SubTypeName": k[2], "GraphStyle": k[3], "Count": v}
            for k, v in type_variant_counts.most_common()
        ],
        "dataSourceCounts": dict(source_counts.most_common()),
        "missingEditorDataSources": missing_sources,
        "formatCounts": [
            {"DataSource": k[0], "Format": k[1], "Status": k[2], "Count": v, "Examples": format_examples[k]}
            for k, v in format_counts.most_common()
        ],
        "sensorTypeCounts": dict(sensor_type_counts.most_common()),
        "graphStyleCounts": dict(graph_style_counts.most_common()),
        "writablePropertiesByType": {typ: dict(counter.most_common()) for typ, counter in writable_by_type.items()},
        "failures": failures,
        "inventory": inventory,
    }

    (REPORT_DIR / "lconnect_asset_layer_inventory.json").write_text(
        json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8"
    )

    with (REPORT_DIR / "lconnect_asset_layer_rows.csv").open("w", newline="", encoding="utf-8-sig") as f:
        writer = csv.DictWriter(f, fieldnames=list(rows[0].keys()) if rows else ["source"])
        writer.writeheader()
        writer.writerows(rows)

    with (REPORT_DIR / "lconnect_decompile_datasource_hits.txt").open("w", encoding="utf-8") as f:
        f.write("\n\n".join(decompile_hits()))

    md = []
    md.append("# L-Connect Theme Asset Layer/Data Source/Format Audit\n")
    md.append(f"- Supporter: `{SUPPORTER}`")
    md.append(f"- Assets found: {len(assets)}")
    md.append(f"- Assets loaded by ThemeEngine: {len(inventory)}")
    md.append(f"- Load failures: {len(failures)}")
    md.append(f"- Asset source buckets: {', '.join(report['sourcesScanned'])}")
    md.append("\n## Layer Types\n")
    for typ, count in type_counts.most_common():
        md.append(f"- `{typ or '(empty)'}`: {count}")
    md.append("\n## Layer Type Variants\n")
    for item in report["typeVariants"][:80]:
        suffix = ", ".join(
            part
            for part in [
                f"TypeName=`{item['TypeName']}`" if item["TypeName"] else "",
                f"SubTypeName=`{item['SubTypeName']}`" if item["SubTypeName"] else "",
                f"GraphStyle=`{item['GraphStyle']}`" if item["GraphStyle"] else "",
            ]
            if part
        )
        md.append(f"- `{item['Type'] or '(empty)'}` {suffix}: {item['Count']}")
    md.append("\n## Data Sources\n")
    for source, count in source_counts.most_common():
        if source in {"", "STATICTEXT"}:
            status = "static/empty"
        elif source in IGNORED_MISSING_SOURCES:
            status = "ignored per request"
        elif source in {norm_source(s) for s in EDITOR_SOURCES}:
            status = "editor source exists"
        else:
            status = "MISSING IN EDITOR"
        md.append(f"- `{source or '(empty)'}`: {count} ({status})")
    md.append("\n## Missing Editor Data Sources\n")
    if missing_sources:
        for item in missing_sources:
            md.append(f"- `{item['DataSource']}`: {item['Count']} layers; examples: {item['Examples'][0]}")
    else:
        md.append("- None, after alias normalization and excluding `VOLUME`/`RAMMODEL`.")
    md.append("\n## Formats Used In Assets\n")
    for item in report["formatCounts"]:
        marker = " **CHECK**" if item["Status"] != "selectable" and item["Status"] != "empty" else ""
        md.append(f"- `{item['DataSource']}` format `{item['Format']}`: {item['Count']} ({item['Status']}){marker}")
    md.append("\n## Sensor Types\n")
    if sensor_type_counts:
        for sensor, count in sensor_type_counts.most_common():
            md.append(f"- `{sensor}`: {count}")
    else:
        md.append("- No non-empty `SensorType` values were found in loaded assets.")
    md.append("\n## Graph Styles\n")
    if graph_style_counts:
        for style, count in graph_style_counts.most_common():
            md.append(f"- `{style}`: {count}")
    else:
        md.append("- No non-empty `GraphStyle` values were found in loaded assets.")
    md.append("\n## Load Failures\n")
    if failures:
        for failure in failures[:50]:
            md.append(f"- `{failure['Path']}`: {failure['Error'][:240]}")
    else:
        md.append("- None.")
    md.append("\n## Files\n")
    md.append("- Full JSON inventory: `reports/lconnect_asset_layer_inventory.json`")
    md.append("- Flat CSV rows: `reports/lconnect_asset_layer_rows.csv`")
    md.append("- Decompile rg hits: `reports/lconnect_decompile_datasource_hits.txt`")
    (REPORT_DIR / "lconnect_asset_datasource_format_report.md").write_text("\n".join(md), encoding="utf-8")

    print(json.dumps({k: report[k] for k in ["assetCount", "loadedCount", "failureCount", "sourcesScanned"]}, indent=2))


if __name__ == "__main__":
    main()
