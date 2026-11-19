"""Retain original XLSX parts around artifact-tool's authored enemy additions.

artifact-tool authors all new cells, formatting and drawings. This post-export
step restores untouched source parts, including font charset metadata, sound
tables, calculation settings and worksheet views that a round trip may omit.
It does not invent workbook data. Requires lxml from Codex's bundled runtime.
"""
from __future__ import annotations
from copy import deepcopy
from pathlib import Path
import re
import zipfile
from lxml import etree as ET

ROOT = Path(__file__).resolve().parents[1]
FOLDER = ROOT / "outputs/graveyard-merge-20261119"
SOURCE = FOLDER / "source.xlsx"
OUTPUT = FOLDER / "讨伐队经理-图鉴（已合并低语墓园）.xlsx"
MAIN = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"
NS = {"m": MAIN}
PARSER = ET.XMLParser(resolve_entities=False, no_network=True)


def read(data: bytes):
    return ET.fromstring(data, PARSER)


def write(element) -> bytes:
    return ET.tostring(element, encoding="UTF-8", xml_declaration=True, standalone=True)


def column(address: str) -> int:
    value = 0
    for char in re.match(r"[A-Z]+", address)[0]:
        value = value * 26 + ord(char) - 64
    return value


def main() -> None:
    with zipfile.ZipFile(SOURCE) as original, zipfile.ZipFile(OUTPUT) as authored:
        contents = {item.filename: original.read(item.filename) for item in original.infolist()}
        original_styles = read(contents["xl/styles.xml"])
        new_styles = read(authored.read("xl/styles.xml"))
        # Imported style indexes must retain the reviewed original collections.
        for name in ("fonts", "fills", "borders", "cellStyleXfs"):
            assert len(original_styles.find("m:" + name, NS)) == len(new_styles.find("m:" + name, NS)), name
        old_xfs = original_styles.find("m:cellXfs", NS)
        exported_xfs = new_styles.find("m:cellXfs", NS)
        for index, xf in enumerate(old_xfs):
            for key in ("fontId", "fillId", "borderId", "numFmtId", "xfId"):
                assert xf.get(key, "0") == exported_xfs[index].get(key, "0"), (index, key)
        original_formats = original_styles.find("m:numFmts", NS)
        existing_formats = {node.get("numFmtId"): node.get("formatCode") for node in original_formats}
        for node in new_styles.find("m:numFmts", NS):
            key = node.get("numFmtId")
            if key in existing_formats:
                assert existing_formats[key] == node.get("formatCode")
            else:
                original_formats.append(deepcopy(node))
        original_formats.set("count", str(len(original_formats)))
        for node in list(exported_xfs)[len(old_xfs):]:
            old_xfs.append(deepcopy(node))
        old_xfs.set("count", str(len(old_xfs)))
        contents["xl/styles.xml"] = write(original_styles)

        target_part = "xl/worksheets/sheet2.xml"
        target = read(contents[target_part])
        exported = read(authored.read(target_part))
        assert target.find("m:dimension", NS).get("ref") == "A1:O24", "Unexpected source enemy layout"
        shared = read(authored.read("xl/sharedStrings.xml"))
        rows = target.find("m:sheetData", NS)
        row_by_id = {node.get("r"): node for node in rows}
        for exported_row in exported.find("m:sheetData", NS):
            row_id = exported_row.get("r")
            row_number = int(row_id)
            new_cells = [node for node in exported_row if row_number > 24 or column(node.get("r")) > 15]
            if not new_cells:
                continue
            if row_id in row_by_id:
                destination = row_by_id[row_id]
            else:
                destination = ET.SubElement(rows, f"{{{MAIN}}}row", attrib=dict(exported_row.attrib))
                row_by_id[row_id] = destination
            destination.set("spans", "1:20")
            for authored_cell in new_cells:
                cell = deepcopy(authored_cell)
                # Leave original shared-string indexes and every untouched sheet intact.
                if cell.get("t") == "s":
                    value = cell.find("m:v", NS)
                    item = shared[int(value.text)]
                    cell.remove(value)
                    cell.set("t", "inlineStr")
                    inline = ET.SubElement(cell, f"{{{MAIN}}}is")
                    for child in item:
                        inline.append(deepcopy(child))
                destination.append(cell)
            destination[:] = sorted(destination, key=lambda cell: column(cell.get("r")))
        rows[:] = sorted(rows, key=lambda row: int(row.get("r")))
        target.find("m:dimension", NS).set("ref", "A1:T54")
        columns = target.find("m:cols", NS)
        for node in exported.findall("m:cols/m:col", NS):
            if int(node.get("min")) > 15:
                columns.append(deepcopy(node))
        drawing = exported.find("m:drawing", NS)
        assert drawing is not None
        # Worksheet child ordering matches the authored sheet; source has no drawings.
        tail = target.find("m:extLst", NS)
        if tail is None:
            target.append(deepcopy(drawing))
        else:
            target.insert(target.index(tail), deepcopy(drawing))
        contents[target_part] = write(target)

        for name in authored.namelist():
            if name.startswith(("xl/drawings/", "xl/media/")) or name == "xl/worksheets/_rels/sheet2.xml.rels":
                assert name not in contents
                contents[name] = authored.read(name)
        content_types = read(contents["[Content_Types].xml"])
        type_keys = {(node.tag, node.get("Extension"), node.get("PartName")) for node in content_types}
        for node in read(authored.read("[Content_Types].xml")):
            key = (node.tag, node.get("Extension"), node.get("PartName"))
            part_name = node.get("PartName", "").lstrip("/")
            if key not in type_keys and (not part_name or part_name in contents):
                content_types.append(deepcopy(node))
        contents["[Content_Types].xml"] = write(content_types)

        temporary = OUTPUT.with_suffix(".native.tmp")
        with zipfile.ZipFile(temporary, "w", zipfile.ZIP_DEFLATED) as result:
            for name, data in contents.items():
                result.writestr(name, data)
    temporary.replace(OUTPUT)
    # Seven unrelated worksheets and the sound table stay byte-for-byte identical.
    changed = {"[Content_Types].xml", "xl/styles.xml", target_part}
    with zipfile.ZipFile(SOURCE) as original, zipfile.ZipFile(OUTPUT) as result:
        for name in original.namelist():
            if name not in changed:
                assert original.read(name) == result.read(name), name
        old = read(original.read(target_part))
        new = read(result.read(target_part))
        for cell in old.findall("m:sheetData/m:row/m:c", NS):
            found = new.find(f'.//m:c[@r="{cell.get("r")}"]', NS)
            # Compare the element, excluding root namespace declarations.
            def canonical(node):
                return node.tag, sorted(node.attrib.items()), node.text, tuple(canonical(child) for child in node)
            assert canonical(cell) == canonical(found), cell.get("r")
    print("Native preservation verified: 7 unrelated sheets, original enemy cells, sound table and original workbook metadata.")


if __name__ == "__main__":
    main()
