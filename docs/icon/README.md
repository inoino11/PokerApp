# アイコンの元データ

- `icon.svg`: 通常のアイコン。`wwwroot/icon-512.png`・`icon-192.png`・`favicon.png`（32px）の元。
- `icon-maskable.svg`: Android のホーム画面で丸く切り抜かれても欠けないよう、全体を 86% に縮めた版。`wwwroot/icon-maskable-512.png`・`icon-maskable-192.png` の元。
- 色はアプリのテーマと同じ緑（#1b6e3c）と白の 2 色。
- PNG は、SVG を Edge で 512px に描いてから縮めて作る（文字は Arial の太字で描かれる）。
