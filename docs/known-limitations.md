# Known Limitations

- 対応対象は YMM4 プロジェクト JSON 内の `FilePath` です。
- 未知の YMM4 内部形式は完全には保証しません。
- YMM4 DLL はリポジトリに同梱しません。
- 複数の `YmmpxLib.dll` を同時配置する運用は非推奨です。
- `MSB3277` の `WindowsBase` 競合警告は、`YmmpxLibPlugin` が YMM4 同梱 DLL と .NET 10 の参照アセンブリの両方を参照するために出ることがあります。
- 現状のビルドでは警告止まりですが、YMM4 配布 DLL 更新時は再確認が必要です。
