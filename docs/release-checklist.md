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
