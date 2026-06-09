# Compatibility Policy

- 1.x 系では公開 API を原則維持する。
- 破壊的変更は 2.0.0 以降で扱う。
- `YmmpxCompatibilityVersion` は過去形式との互換維持のための制御点として使う。
- 現在 `Latest` を既定にし、`V0_1` / `V0_2` は `Latest` へフォールバックする。
