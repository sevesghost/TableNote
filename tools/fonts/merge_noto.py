#!/usr/bin/env python3
"""
Builds Assets/Fonts/NotoSansThai-{Regular,Bold}.ttf: Noto Sans Thai with the Latin of Noto Sans merged in,
plus the few symbols the program shows (arrows, check marks, ▸ ▾, ⚠, ≈ ≤ ⋮).

Why: Noto Sans Thai has Thai glyphs only (no digits, no Latin letters, no . / : % ( )). Everything else in a line
fell back to another font (Segoe UI on Windows), so "วันที่ 17/11/2569" mixed two designs. Noto Sans is the Latin
the Noto Thai design was drawn to match. With both in one file, every text box, menu, popup and exported picture
uses one design, with no font fallback for ordinary text. Colour emoji (📅 🔁 ⏰ 🔔) still come from the system.

Sources (SIL Open Font License 1.1, no Reserved Font Name; see Assets/Fonts/OFL.txt), all from
https://raw.githubusercontent.com/notofonts/notofonts.github.io/main/fonts/<Family>/hinted/ttf/ :
  Noto Sans Thai 2.000   https://github.com/notofonts/thai   (the original files: git history of Assets/Fonts)
  Noto Sans              NotoSans-Regular.ttf / NotoSans-Bold.ttf     (Latin, digits, punctuation)
  Noto Sans Symbols      NotoSansSymbols-Regular.ttf / -Bold.ttf      (arrows)
  Noto Sans Math         NotoSansMath-Regular.ttf                     (≈ ≤ ≥ ≠ ⋮)
  Noto Sans Symbols 2    NotoSansSymbols2-Regular.ttf                 (✓ ✕ ☑ ⚠ ▸ ▾ …)

Usage:
  python3 tools/fonts/merge_noto.py <NotoSansThai-X.ttf> <NotoSans-X.ttf> <out.ttf> [symbol font ...]
Example (Regular):
  python3 tools/fonts/merge_noto.py NotoSansThai-Regular.ttf NotoSans-Regular.ttf Assets/Fonts/NotoSansThai-Regular.ttf \\
      NotoSansSymbols-Regular.ttf NotoSansMath-Regular.ttf NotoSansSymbols2-Regular.ttf
(Bold: the Bold Thai, Latin and Symbols files; Math and Symbols 2 have only Regular.)
Each symbol font gives only the SYMBOLS it has that an earlier font did not.
Needs fontTools (pip install fonttools). Hinting is dropped (Avalonia's Skia renderer does not use it).
"""
import os
import sys
from fontTools import subset
from fontTools.merge import Merger
from fontTools.ttLib import TTFont

LATIN = (
    list(range(0x0020, 0x007F)) + list(range(0x00A0, 0x0180)) +   # Basic Latin, Latin-1, Latin Extended-A
    list(range(0x2000, 0x2070)) + list(range(0x20A0, 0x20C1)) +   # General Punctuation, currency
    list(range(0x2100, 0x2150)) + [0x2212, 0x2215, 0xFFFD]        # letterlike, minus, slash, replacement
)

# Symbols used in menus, cells, the help text and the change log. Emoji are left to the system emoji font.
SYMBOLS = (
    list(range(0x2190, 0x219A)) + [0x21A9, 0x21AA, 0x21BB, 0x21C4] +           # ← ↑ → ↓ ↔ ↕ ↖ ↗ ↘ ↙ ↩ ↪ ↻ ⇄
    [0x2248, 0x2260, 0x2264, 0x2265, 0x22EE, 0x22EF] +                         # ≈ ≠ ≤ ≥ ⋮ ⋯
    [0x25A0, 0x25A1, 0x25B2, 0x25B4, 0x25B6, 0x25B8, 0x25BC, 0x25BE, 0x25C0, 0x25C2, 0x25CB, 0x25CF] +  # ■ □ ▲ ▴ ▶ ▸ ▼ ▾ ◀ ◂ ○ ●
    [0x2605, 0x2606, 0x2610, 0x2611, 0x2612, 0x26A0] +                         # ★ ☆ ☐ ☑ ☒ ⚠
    [0x2713, 0x2714, 0x2715, 0x2716, 0x2717, 0x2718]                           # ✓ ✔ ✕ ✖ ✗ ✘
)


def prepared(path, unicodes, out):
    font = TTFont(path)
    options = subset.Options()
    options.hinting = False
    options.layout_features = ["*"]
    options.name_IDs = ["*"]
    options.name_languages = ["*"]
    options.notdef_outline = True
    options.glyph_names = True
    options.drop_tables += ["DSIG", "MATH", "vhea", "vmtx"]  # vertical metrics: only some symbol fonts have them
    cmap = font.getBestCmap()
    sub = subset.Subsetter(options)
    sub.populate(unicodes=[u for u in unicodes if u in cmap])
    sub.subset(font)
    font.save(out)
    return out


def main(thai_path, latin_path, out_path, *symbol_paths):
    thai = TTFont(thai_path)
    thai_tmp = prepared(thai_path, list(thai.getBestCmap()), out_path + ".thai.tmp")
    latin_tmp = prepared(latin_path, LATIN, out_path + ".latin.tmp")
    parts = [thai_tmp, latin_tmp]  # the Thai font first: its names, Thai cmap entries win
    taken = set(TTFont(thai_tmp).getBestCmap()) | set(TTFont(latin_tmp).getBestCmap())
    for i, path in enumerate(symbol_paths):
        have = TTFont(path).getBestCmap()
        wanted = [u for u in SYMBOLS if u in have and u not in taken]
        if not wanted:
            continue
        taken.update(wanted)
        parts.append(prepared(path, wanted, f"{out_path}.sym{i}.tmp"))
    merged = Merger().merge(parts)

    # Line metrics: Thai needs the deeper descent, accented Latin capitals the taller ascent.
    # The symbol fonts do not change them (their glyphs sit inside the text line).
    latin = TTFont(latin_tmp)
    t_hhea, l_hhea = TTFont(thai_tmp)["hhea"], latin["hhea"]
    ascent = max(t_hhea.ascent, l_hhea.ascent)
    descent = min(t_hhea.descent, l_hhea.descent)
    merged["hhea"].ascent, merged["hhea"].descent, merged["hhea"].lineGap = ascent, descent, 0
    os2, l_os2 = merged["OS/2"], latin["OS/2"]
    os2.sTypoAscender, os2.sTypoDescender, os2.sTypoLineGap = ascent, descent, 0
    os2.usWinAscent = max(TTFont(thai_tmp)["OS/2"].usWinAscent, l_os2.usWinAscent)
    os2.usWinDescent = max(TTFont(thai_tmp)["OS/2"].usWinDescent, l_os2.usWinDescent)

    names = merged["name"]
    version = names.getDebugName(5) or "Version 2.000"
    latin_version = latin["name"].getDebugName(5) or ""
    style = names.getDebugName(2) or "Regular"
    for record in list(names.names):
        if record.nameID == 5:
            names.setName(f"{version.split(';')[0]}; Thai + Noto Sans Latin ({latin_version.split(';')[0]}) + Noto symbols for TableNote",
                          5, record.platformID, record.platEncID, record.langID)
        elif record.nameID == 3:
            names.setName(f"Noto Sans Thai {style} + Noto Sans Latin;TableNote", 3, record.platformID, record.platEncID, record.langID)
    merged.save(out_path)
    for tmp in parts:
        os.remove(tmp)
    check = TTFont(out_path).getBestCmap()
    missing = [hex(c) for c in (ord(ch) for ch in "0123456789 AZaz./:%()+-ก๙") if c not in check]
    no_symbol = [chr(c) for c in SYMBOLS if c not in check]
    print(f"{out_path}: {len(check)} characters, ascent {ascent}, descent {descent}",
          "MISSING " + str(missing) if missing else "ok",
          ("(symbols not found: " + " ".join(no_symbol) + ")") if no_symbol else "")


if __name__ == "__main__":
    if len(sys.argv) < 4:
        sys.exit(__doc__)
    main(*sys.argv[1:])
