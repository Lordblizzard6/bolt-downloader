#!/usr/bin/env bash
set -euo pipefail

# Empaqueta la extensión para Chromium/Chrome (Linux/Mac)
# Requisitos: bash, zip
# Uso: ./build.sh [salida.zip]

here="$(cd "$(dirname "$0")" && pwd)"
cd "$here"

# Extrae versión desde manifest.json (sin jq)
version="$(grep -oE '"version"\s*:\s*"[^"]+"' manifest.json | head -n1 | sed -E 's/.*"([^"]+)"/\1/')"
[ -n "$version" ] || version="0.0.0"

out="${1:-bolt-helper_${version}.zip}"

# Carpeta temporal
work=".build-tmp"
rm -rf "$work"
mkdir -p "$work"

# Copiar archivos necesarios
cp -f manifest.json "$work/"
cp -f background.js "$work/"
cp -f content.js "$work/"
cp -f popup.html "$work/" || true
cp -f popup.js "$work/" || true
cp -f silk2_32_mm0.png "$work/" || true
cp -f silk2_48_mm0-01.png "$work/" || true
cp -f silk2_64_mm0.png "$work/" || true
cp -f silk2_128_mm0.png "$work/" || true

# Locales
mkdir -p "$work/_locales"
for lang in en es de fr; do
	if [ -d "_locales/$lang" ]; then
		mkdir -p "$work/_locales/$lang"
		cp -f "_locales/$lang/messages.json" "$work/_locales/$lang/" || true
	fi
done

# Crear ZIP
rm -f "$out"
(cd "$work" && zip -rq9 "../$out" .)

# Limpieza
rm -rf "$work"

echo "OK -> $out"
