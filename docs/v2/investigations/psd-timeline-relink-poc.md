# PSD Timeline 再リンク PoC

## 目的と範囲

Ymmpx v2 で PSDTool 対応立ち絵の PSD 参照先を変更するとき、YMM4 が公開している
操作経路だけで、PSD のレイヤー設定を保持したまま安全に再リンクできるかを調査する。

この文書は production API の仕様ではない。`IYmmBridge`、`YmmpxRuntime`、v1 の
production コードは変更していない。YMM4 内部型、private API、Reflection、JSON の強制書換えは
使用していない。

## 検証環境

- YMM4: v4.55.1.1 Lite
- .NET SDK: 10.0.400（リポジトリの v2 基盤と同一）
- 入力プロジェクト: `psdテスト.ymmp` の隔離コピー
- PSD: `pl_chibi_aoi_im11221956_v6.02_20251110.psd`

元プロジェクトと元 PSD は変更しない。検証用には一時ディレクトリへ `baseline.ymmp` と、
別パス `moved\\aoi.psd` をコピーした。SHA-256 とファイルサイズが一致することを確認した。

## 初期状態の観測

隔離コピーの YMMP には PSDTool の `TachieItem` が 1 件あり、立ち絵名は `琴葉葵` である。

- Timeline Item の `FilePath` は元 PSD を指す。
- Timeline Item の `EnableLayers` は 19 件。
- Timeline Item の `EnableLayerPaths` は 19 件で、日本語レイヤー名を含む。
- Item の位置は Frame 0、Layer 0、Length 300。
- Character 側の tachie parameter も同じ PSD の `FilePath` を持つ。一方、default item / face
  parameter の PSD 状態は空である。

従って、Character 側と Timeline Item 側を同一の状態として扱わず、両方を比較対象にする必要がある。

## Baseline

### パス完全一致

入力 YMMP の保存状態では、Item と Character の参照先は同じ既存 PSD パスであり、上記の
`EnableLayers` と `EnableLayerPaths` が記録されている。これは再リンクを行わない構造上の正常系
として採用する。

YMM4 GUI での保存・終了・再起動・再読込までの確認は、この調査時点では完了していない。
従って「再起動後も保持された」という実測結果ではない。

### 通常の参照先変更

未実施。隔離コピーを用意し YMM4 を起動したが、現在の UI Automation 環境では YMM4 の
ファイルメニュー項目のアクセシビリティ要素を安定して実行できず、通常の GUI 再リンクを
完遂できなかった。推測で結果を補完していない。

## PoC 1: Item 削除 → 参照変更 → 復元

未実施。Timeline Item の削除はローカルデータを破壊し得る操作であるため、隔離コピーであっても
実行直前に明示確認を得る必要がある。また、公開 Plugin API に後述の操作経路が見つからないため、
仮に手動 GUI 操作が成功しても Plugin 自動化の根拠にはならない。

このため、以下は未確認である。

- `EnableLayers` / `EnableLayerPaths` の保持
- Character / Item identity
- Timeline の位置、Layer、Frame、Length、その他 Item 設定
- 保存後および再起動後の状態
- Undo / Redo

## 公開 YMM4 Plugin API 調査

`libs/YMM4/YukkuriMovieMaker.Plugin.dll` の公開 interface 一覧を静的に確認した。
Timeline に関連する公開契約として `YukkuriMovieMaker.Player.Video.ITimelineSource` は存在するが、
Plugin が既存 Project の Timeline Item を取得、削除、追加、復元、または Undo/Redo transaction に
参加するための公開 Plugin interface は確認できなかった。

確認できた Plugin 用 interface は `IPlugin`、`IToolPlugin`、`ITachiePlugin` 等であり、既存
Timeline を操作する host service / command / registry 契約は含まれていない。この調査では内部実装の
解析や非公開 API の利用を行っていない。

したがって、今回の条件（公開 API のみ、Reflection なし、private API なし）では、
`Item 削除 → FilePath 変更 → Item 復元` を Plugin から同等に実行する根拠を確認できなかった。

## YMMP 差分

この PoC では YMMP の再リンク操作を完遂していないため、操作後 YMMP 差分は存在しない。
初期状態として確認した意味のあるフィールドは以下である。

- Character tachie parameter の `FilePath`
- `TachieItem` parameter の `FilePath`
- `EnableLayers`
- `EnableLayerPaths`
- Item の Frame / Layer / Length

## 判定

### Plugin 自動化

**Not viable（今回確認できた公開 API の範囲）**。

公開 Plugin API だけでは、既存 Timeline Item を安全に remove / modify / restore し、Undo/Redo と
保存通知に整合させる操作経路を確認できない。ここを private API や Reflection で補うことは、
`reflection-boundary.md` と今回の PoC 条件に反するため採用しない。

### 手動 GUI 回避手順

未判定。Item 削除を含む GUI 操作、保存、YMM4 再起動後の確認が未実施のため、手動回避手順としても
安全とは結論しない。

## 影響と次の判断

- `IYmmBridge` に PSD 再リンク API を追加しない。
- v1 / v2 の production コードを変更しない。
- 現時点で Timeline 自動再リンク方式を production に採用しない。
- YMM4 が既存 Item 操作と Undo/Redo を正式に公開する API を提供した場合に限り、同じ最小ケースで
  PoC を再実施する。
- それまでは、Dependency Recovery またはユーザーによる手動再リンク支援を別案として検討する。

## 未解決事項

- 隔離コピー上での通常再リンク後の保存・再起動結果
- Item 削除 → 再リンク → 復元という手動 GUI 手順の安全性
- Character を複数 Timeline Item が共有するケース
- YMM4 将来版における公開 Timeline 編集 API の有無
