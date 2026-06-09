# Release Checklist

- [ ] dotnet restore
- [ ] dotnet build -c Release
- [ ] dotnet test -c Release
- [ ] sample .ymmp can be packaged into .ymmpx
- [ ] sample .ymmpx can be extracted
- [ ] restored .ymmp FilePath values point to extracted resources
- [ ] YmmpxLibPlugin loads in YMM4
- [ ] release assets include README.md and LICENSE.txt
- [ ] release is not marked as prerelease
- [ ] release artifacts are `YmmpxLib-v1.0.0.zip` and `YmmpxLibPlugin-v1.0.0.ymme`
- [ ] YMM4 実機で plugin の表示名が `YmmpxLib Shared Library` であることを確認する
- [ ] YMM4 実機で package / extract の往復後に `FilePath` が壊れていないことを確認する
- [ ] YMM4 実機で `links.json` / `manifest.json` / `links.txt` のどれでも復元できることを確認する
