# Ymmpx v2 利用側プラグインの optional dependency 調査

## 結論

YMM4 v4.55.1.1 Lite では、`YmmpxLib` を通常参照した ConsumerPlugin を、`YmmpxLib` と `YmmpxLibPlugin` がない状態で配置しても、今回の最小 `IPlugin` は YMM4 の起動を止めなかった。

ただし、`YmmpxLib` API を呼ぶメソッドの本体に置いた `try/catch` は安全な未導入判定にならなかった。CLR がメソッド本体を実行する前に `YmmpxLib` を解決し、`FileNotFoundException` を送出したため、Consumer 内の catch 節には到達しなかった。

そのため、Consumer が未導入時に自ら確実に案内を表示する必要がある v2 の標準方式としては、Consumer 本体から `YmmpxLib` への静的参照を除く必要がある。現時点の推奨は、Consumer 側の限定した動的探索（Reflection）を継続すること。Bridge/Contracts/Adapter は、実際のYMM4ロードモデルを使った追加検証を終えるまで本実装しない。

## 対象と非対象

- 対象: `ConsumerPlugin -> YmmpxLib` の optional dependency。
- 非対象: `YmmpxLibPlugin -> YmmpxLib`。この2 DLL は同一パッケージで配布する前提であり、壊れた同一パッケージ内欠損は扱わない。
- production API、Bridge、Contracts、Adapter は変更していない。

## 現在の依存構造

- `YMMPXLib/YmmpxLib.csproj`: `net10.0` のライブラリ。外部参照なし。
- `YMMPXLibPlugin/YmmpxLibPlugin.csproj`: `YmmpxLib` への `ProjectReference` と、YMM4 SDK の `YukkuriMovieMaker.Plugin.dll` へのコンパイル時参照を持つ。
- `YmmpxLibPlugin` 実装は `IPlugin` の名前提供のみで、現在は `YmmpxLib` 型を使用していない。
- `YMMPXCli` と README のサンプルは `ProjectReference`/`using YmmpxLib` による通常参照である。ConsumerPlugin向けの既存利用例やConsumer側のReflection実装は、このリポジトリには存在しない。

## YMM4 のロードモデル（静的確認）

YMM4 v4.55.1.1 の `PluginAssemblyLoader` は、`user/plugin` 配下の全 `*.dll` を再帰検索し、各ファイルを `Assembly.LoadFrom` する。ロード例外はログに記録し、その Assembly のみを除外する。

続く `PluginLoader` の静的初期化では、ロード済み Assembly に対して `GetTypes()`、`GetInterfaces()`、`Activator.CreateInstance()` を行って `IPlugin` を収集する。この初期化は `App.OnStartup` の `PluginLoader.LocalizePlugins` 参照で、アプリ起動時に実行される。YMM4の `AssemblyResolve` ハンドラ登録は、この初期化より後である。

## 検証環境

- YMM4: `YukkuriMovieMaker.exe` 4.55.1.1 Lite。
- .NET runtime: 10.0.10。SDK: 10.0.400。
- Consumer: `IPlugin` の最小実装。`YmmpxLibraryInfo.AssemblyVersion` を呼ぶため、ILに `[YmmpxLib]` の AssemblyRef が残ることを確認した。
- 配置先: `C:\Users\yu-za-hazimeyou\Desktop\YukkuriMovieMaker_v4_Lite\user\plugin\YmmpxLibPlugin`。
- 未導入ケースでは、既存の `YmmpxLib.dll`、`YmmpxLib.deps.json`、`YmmpxLibPlugin.dll`、`YmmpxLibPlugin.deps.json` を起動中だけ一時退避し、各試験後に4/4復元した。

## 実測結果

| ケース | YmmpxLib DLL | YMM4起動 | Consumerロード | YmmpxLib呼出 |
|---|---|---|---|---|
| メソッド内部参照 | あり | 15秒後も継続 | 成功 | 未導入判定の対象外 |
| メソッド内部参照 | なし | 12秒後も継続 | 成功 | `FileNotFoundException`、Consumer catch 未到達 |
| private field | なし | 12秒後も継続 | 成功 | 未呼出 |
| public property | なし | 12秒後も継続 | 成功 | 未呼出 |
| interface method signature | なし | 12秒後も継続 | 成功 | 未呼出 |

Consumerロードの「成功」は、YMM4が起動時に `PluginLoader` を初期化し、全 `IPlugin` を列挙・生成する実装であること、および該当起動ログにConsumerのロード失敗がないことに基づく。プラグイン一覧画面での目視表示は今回未確認。

`ExecuteWithTryCatch()` を `MethodInfo.Invoke` した実測の例外連鎖は以下だった。

1. `System.Management.Automation.MethodInvocationException`
2. `System.IO.FileNotFoundException`
3. `FileName`: `YmmpxLib, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null`

呼出対象メソッド内に `catch (FileNotFoundException | FileLoadException | TypeLoadException)` を置いてもcatchされなかった。依存Assembly解決が、tryブロックの実行前に必要になったことを示す。

YMM4ログには、今回のConsumerやYmmpxLibに関する `FileNotFoundException`、`FileLoadException`、`TypeLoadException`、`ReflectionTypeLoadException` は記録されなかった。既存の `ObjLoader/assimp.dll` と `YMMKeyboardPlugin` に関する別プラグインの既知ログだけが出力された。

## 候補比較

| 案 | 実現可能性 | YMM4起動安全性 | 未導入時の案内 | Reflection | 主な判断 |
|---|---|---|---|---|---|
| A. 通常参照 | Medium | 今回のYMM4ではHigh | Low | 不要 | 型探索までは通ったが、API呼出時のcatchに到達できない。YMM4将来版の型探索変更にも弱い。 |
| B. Reflection継続 | High | High | High | 必要 | Consumer本体に静的AssemblyRefを残さず、探索失敗を通常制御として扱える。API文字列と戻り値の契約を小さく固定する。 |
| C. Adapter Assembly分離 | Medium | Medium | Medium | Consumer本体では不要にできる | YMM4は`user/plugin`配下の全DLLを再帰的に`Assembly.LoadFrom`する。Adapter自体のロード失敗はYMM4が個別ログ化して続行する見込みだが、ConsumerからAdapterを静的参照すれば同じ問題が戻る。実パッケージ配置での追加実測が必要。 |
| D. Bridge / Registration | Medium | Medium | Medium | Bridgeの配置次第 | YmmpxLibパッケージ不在時にConsumerが参照できる常駐Bridgeが必要。YmmpxLibPluginだけをBridgeにしても、パッケージ不在時のConsumer案内は解決しない。 |
| Contracts Assembly | Medium | Medium | Medium | ConsumerからYmmpxLib参照は不要 | Contractsが必須DLLとなる。未導入の完全自立を保証せず、DLLと配布管理を増やす。production追加は保留。 |

## 推奨

v2のConsumer向けには、当面「Consumer本体はYmmpxLibを静的参照しない。限定したReflectionで `YmmpxLib` を探索し、型・APIバージョン・必要メソッドを検証し、失敗時は日本語のインストール案内を出す」を推奨する。

Reflectionを使う箇所は1か所の小さな互換レイヤーに閉じ込め、例外型、探索Assembly名、検出APIバージョン、失敗理由をログに残す。これはReflectionの無条件な維持ではなく、YMM4を停止させずに未導入時の案内を可能にするための境界である。

## v2設計への影響と未解決事項

- Core / Plugin Bridgeの本実装は、この調査だけでは開始しない。
- Consumer側Reflectionは、現時点では削除しない。
- Adapterを採用するなら、ConsumerがAdapterを静的参照しない構造、YMM4がAdapter単体を失敗隔離すること、配布DLL順・競合を実パッケージで検証する必要がある。
- Contract Assemblyも同様に、必須DLL化とConsumer作者の配布負担を比較してから判断する。
- YMM4 v4.55.1.1以外、または`IToolPlugin`等でホストがシグネチャを反射列挙する経路は未検証。
