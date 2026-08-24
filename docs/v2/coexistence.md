# Ymmpx v1 / v2 共存方針

## 方針

v1.0.1 はv1系の最終版として維持します。v2はv1をrename・置換・破壊的更新するものではありません。

v2はv1 APIとのソース互換・バイナリ互換を要求しません。代わりに、v1とv2を同じYMM4環境へ同時導入できることで、既存Consumerを更新せず段階的に移行できるようにします。

```text
既存Consumer ─→ YmmpxLib.dll (v1)
新Consumer   ─→ YmmpxLibV2.dll (v2)

YMM4
├─ YmmpxLib.dll
├─ YmmpxLibPlugin.dll
├─ YmmpxLibV2.dll
└─ YmmpxLibV2Plugin.dll
```

## 分離規則

- v1: Assembly名 `YmmpxLib`、Plugin Assembly名 `YmmpxLibPlugin`、namespace `YmmpxLib` / `YmmpxLibPlugin`。
- v2: Assembly名 `YmmpxLibV2`、Plugin Assembly名 `YmmpxLibV2Plugin`、namespace `YmmpxLibV2` / `YmmpxLibV2.Plugin`。
- v1のproductionコード、公開API、Assembly名、namespaceはv2のために変更しない。
- v2 CoreはYMM4およびv2 Pluginを参照しない。v2 PluginだけがYMM4とv2 Coreを参照する。

## Reflection境界との関係

v2 ConsumerがYmmpxLibV2をoptional dependencyとして扱う場合、Reflectionを許可するのは存在・互換性確認だけです。API実行やBridge呼び出しにReflectionを使いません。詳細は [Reflection利用境界](reflection-boundary.md) を参照してください。

## 移行

既存v1 Consumerは変更不要です。新しい機能が必要なConsumerだけがYmmpxLibV2を選択します。v1/v2を同時導入した状態でAssembly名・Plugin型名が衝突しないことを自動テストで確認します。

YMM4 GUIでの実パッケージ共存確認は、リリース前に対象YMM4バージョンで別途実施します。今回の基盤ではGUI確認を自動化しません。
