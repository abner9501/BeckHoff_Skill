import argparse
import re
import uuid
from pathlib import Path


MODULE_TYPES = {
    "EL3078": (8, "LREAL"),
    "EL4134": (4, "LREAL"),
    "EL1008": (8, "BOOL"),
    "EL2008": (8, "BOOL"),
}


DEFAULT_ORDER = [
    ("EL3078", 2),
    ("EL4134", 3),
    ("EL1008", 5),
    ("EL1008", 6),
    ("EL1008", 7),
    ("EL1008", 8),
    ("EL1008", 10),
    ("EL1008", 11),
    ("EL1008", 12),
    ("EL1008", 13),
    ("EL1008", 14),
    ("EL2008", 15),
    ("EL2008", 16),
    ("EL2008", 17),
    ("EL2008", 19),
    ("EL2008", 20),
    ("EL2008", 21),
    ("EL2008", 22),
    ("EL2008", 23),
    ("EL2008", 24),
]


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig")


def parse_ethercat_order(path: Path | None) -> list[tuple[str, int]]:
    if not path or not path.exists():
        return DEFAULT_ORDER
    text = read_text(path)
    items: list[tuple[str, int]] = []
    for match in re.finditer(r'<Box Id="(\d+)".*?>\s*<Name>Term\s+(\d+)\s+\((EL\d+)\)</Name>', text, re.S):
        term_no = int(match.group(2))
        module = match.group(3)
        if module in MODULE_TYPES:
            items.append((module, term_no))
    if not items:
        return DEFAULT_ORDER
    seen = set()
    ordered = []
    for module, term_no in items:
        key = (module, term_no)
        if key not in seen:
            ordered.append(key)
            seen.add(key)

    return ordered


def parse_bindings(reference_pou: Path) -> dict[str, dict[int, str]]:
    text = read_text(reference_pou)
    bindings: dict[str, dict[int, str]] = {}
    for match in re.finditer(r'^\s*(EL\d+_t\d+)\[(\d+)\]\s*:=\s*(.*?);', text, re.M):
        var_name = match.group(1)
        channel = int(match.group(2))
        source = match.group(3).strip()
        bindings.setdefault(var_name, {})[channel] = source
    return bindings


def build_declaration(order: list[tuple[str, int]]) -> str:
    lines = ["FUNCTION_BLOCK FB_IOMap", "VAR_INPUT"]
    for module, term_no in order:
        count, data_type = MODULE_TYPES[module]
        lines.append(f"\t{module}_t{term_no} : ARRAY[1..{count}] OF {data_type};")
    lines.extend(["END_VAR", "VAR_OUTPUT", "END_VAR", "VAR", "END_VAR"])
    return "\n".join(lines)


def build_cyclic(order: list[tuple[str, int]], bindings: dict[str, dict[int, str]]) -> str:
    lines = ["// FB_IOMap.Cyclic", ""]
    for module, term_no in order:
        var_name = f"{module}_t{term_no}"
        count, _ = MODULE_TYPES[module]
        lines.append(f"// ===== {var_name} (Term {term_no}) =====")
        for channel in range(1, count + 1):
            source = bindings.get(var_name, {}).get(channel)
            if source:
                lines.append(f"\t{var_name}[{channel}] := {source};")
            else:
                lines.append(f"\t// {var_name}[{channel}] 未绑定")
        lines.append("")
    return "\n".join(lines)


def build_pou(declaration: str, cyclic: str) -> str:
    pou_id = str(uuid.uuid4()).lower()
    method_id = str(uuid.uuid4()).lower()
    return f'''<?xml version="1.0" encoding="utf-8"?>
<TcPlcObject Version="1.1.0.1">
  <POU Name="FB_IOMap" Id="{{{pou_id}}}" SpecialFunc="None">
    <Declaration><![CDATA[{declaration}]]></Declaration>
    <Implementation><ST><![CDATA[]]></ST></Implementation>
    <Method Name="Cyclic" Id="{{{method_id}}}">
      <Declaration><![CDATA[METHOD Cyclic
]]></Declaration>
      <Implementation>
        <ST><![CDATA[{cyclic}
]]></ST>
      </Implementation>
    </Method>
    <LineIds Name="FB_IOMap"><LineId Id="2" Count="0" /></LineIds>
    <LineIds Name="FB_IOMap.Cyclic"><LineId Id="3" Count="0" /><LineId Id="2" Count="0" /></LineIds>
  </POU>
</TcPlcObject>
'''


def ensure_plcproj_compile(plcproj: Path) -> bool:
    text = read_text(plcproj)
    include = r'Application\Sequence\Subitems\FB_IOMap.TcPOU'
    if include in text:
        return False
    block = (
        f'    <Compile Include="{include}">\n'
        "      <SubType>Code</SubType>\n"
        "    </Compile>\n"
    )
    anchor = '    <Compile Include="Application\\Sequence\\Subitems\\FB_ManualPowerEx.TcPOU">'
    idx = text.find(anchor)
    if idx == -1:
        item_group_end = text.find("  </ItemGroup>")
        if item_group_end == -1:
            raise RuntimeError("Could not find plcproj ItemGroup to insert FB_IOMap compile entry")
        text = text[:item_group_end] + block + text[item_group_end:]
    else:
        line_start = text.rfind("\n", 0, idx) + 1
        text = text[:line_start] + block + text[line_start:]
    plcproj.write_text(text, encoding="utf-8")
    return True


def validate(pou_path: Path, order: list[tuple[str, int]]) -> list[str]:
    text = read_text(pou_path)
    errors = []
    for module, term_no in order:
        var_name = f"{module}_t{term_no}"
        count, data_type = MODULE_TYPES[module]
        decl = f"{var_name} : ARRAY[1..{count}] OF {data_type};"
        if decl not in text:
            errors.append(f"missing declaration: {decl}")
        for channel in range(1, count + 1):
            if f"{var_name}[{channel}] :=" not in text and f"{var_name}[{channel}] 未绑定" not in text:
                errors.append(f"missing channel line: {var_name}[{channel}]")
    return errors


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--target-root", required=True)
    parser.add_argument("--reference-pou", required=True)
    parser.add_argument("--ethercat-text")
    args = parser.parse_args()

    target_root = Path(args.target_root)
    reference_pou = Path(args.reference_pou)
    ethercat_text = Path(args.ethercat_text) if args.ethercat_text else None
    plc_root = target_root / "Tc3_PlasticApplication"
    subitems = plc_root / "Application" / "Sequence" / "Subitems"
    pou_path = subitems / "FB_IOMap.TcPOU"
    plcproj = plc_root / "Tc3_PlasticApplication.plcproj"

    order = parse_ethercat_order(ethercat_text)
    bindings = parse_bindings(reference_pou)
    declaration = build_declaration(order)
    cyclic = build_cyclic(order, bindings)
    pou_path.write_text(build_pou(declaration, cyclic), encoding="utf-8")
    compile_added = ensure_plcproj_compile(plcproj)
    errors = validate(pou_path, order)
    if errors:
        print("Validation failed:")
        for error in errors:
            print(f"  - {error}")
        return 1

    print(f"Updated: {pou_path}")
    print(f"PLC project compile entry: {'added' if compile_added else 'already present'}")
    print(f"Modules: {len(order)}")
    print(f"Bound channels: {sum(len(v) for v in bindings.values())}")
    print("Validation OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
