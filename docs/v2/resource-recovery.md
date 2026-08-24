# Resource Recovery Candidate

## 目的と境界

`ResourceRecoveryService` は、欠損または再取得したい1つの`PackageManifestResource`に対し、ローカルPC上の候補を提示するCore APIである。

```text
PackageManifestResource
        ↓ ResourceIdentity
LocalResourceSearch
        ↓
ResourceRecoveryResult / Candidates
```

今回の責務はSearchだけである。SelectionとApplyは別工程であり、候補を自動採用しない。`SingleCandidate`は「SHA-256一致候補が1件」という事実を示すだけで、FilePath変更やpackage更新を許可するものではない。

## 同一性と検索範囲

- `Length`はSHA-256計算前の絞り込みに使う。
- SHA-256（64文字・大文字16進）が最終的な同一性条件である。
- filename、extension、Resource Kindは同一性の追加条件にしない。rename済みfileも候補にできる。
- `OriginalPath`があれば既存LocalResourceSearchが最初にhash確認する。存在だけでは候補にしない。
- search rootは呼び出し側が明示指定する。再帰検索するがReparse Pointは辿らない。
- PC全体、ユーザープロファイル、Downloads/Documents、registry由来のrootを自動探索しない。

CoreはYMMResourcePackagerの`ExtractedProjects`や`ResourceCache`の具体pathを知らない。将来ResourceCacheを使う場合も、呼び出し側が単なるsearch rootとして渡す。

## Result

`ResourceRecoveryResult`は対象resource、候補、issue、search rootを返す。`MatchKind`に候補数を残すため、PartialFailureでも0/1/複数候補を確認できる。

| Outcome | 意味 |
| --- | --- |
| NotFound | 一致候補なし。正常結果。 |
| SingleCandidate | SHA-256一致候補が1件。自動採用しない。 |
| MultipleCandidates | 一致候補が複数。全候補を返し、選択しない。 |
| PartialFailure | 一部root等でissueがあったが検索は継続。候補も返せる。 |
| Failed | 有効な検索rootがなく検索不能。 |
| UnsupportedResourceKind | Plugin resource。通常素材Recoveryの対象外。 |

`ResourceRecoveryCandidate`はmanifest resourceと既存`ResourceSearchMatch`を保持する。ImageSequenceは現在frame単位で検索し、`groupId`を候補から失わない。sequence全体の充足判定はまだ行わない。

## Read-only

この層はcandidate file、project、manifest、package、search rootを変更しない。file copy、ResourceCache作成、hash cache、auto selection、Project FilePath更新、YMM4操作は非対象である。CoreからConsoleログも出力しない。issueは構造化Resultで利用側へ返す。

Plugin Dependency Recovery、Plugin Portal、GitHub Release、外部素材取得は通常ファイルのLocal Recoveryと責務が異なるため含めない。

## 互換性

Recovery Candidateはruntime APIであり、YMMPX Format 2.0とmanifest schema 1を変更しない。既存の[Dependency Recovery Local Search](dependency-recovery-local-search.md)のsearch安全性と、[Compatibility](compatibility.md)のpackage互換性境界を維持する。

次工程で検討するのは、Candidate ResultからSelection Resultを作ることである。Selection後のFilePath適用やResourceCacheコピーはさらに後段として分離する。
