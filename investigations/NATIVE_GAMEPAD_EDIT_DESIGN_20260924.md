# 純正GAME binding編集 設計書（DESIGN ONLY）2026-09-24

目的: MOD Settingsから純正GAME key binding（純正Controller Key Config「GAMEPAD」タブ）を安全に編集するための設計を確定する。

**今回はDESIGN ONLY。** native呼出（`ChangeKey`等）・ゲームメモリwrite・memory patch・Settings編集UI実装・build・deploy・commit・push・branch切替・stash操作は一切行っていない。追加の静的解析は`GameAssembly.dll`（SHA-256 `59ADBB5B…A9FC4`、canonical）と`global-metadata.dat`のread-only disassembly/metadata読み取りのみ（scratchpad内のPythonツール、リポジトリ外）。

前提資料: `investigations/NATIVE_GAMEPAD_CONFIG_DATAFLOW_20260922.md`（以下「DATAFLOW」）§1〜§20、`investigations/GAMEBINDING_INDEX_MAP_20260921.md`（以下「INDEX_MAP」）、`investigations/ghidra/*_decompile.txt`。

Evidence表記:
- **CONFIRMED**: byte-exact disassembly / metadata / 実機ログで直接確認
- **STRONGLY SUPPORTED**: 複数の独立した状況証拠が一致、直接確認は未了
- **HYPOTHESIS**: 根拠はあるが未検証
- **UNRESOLVED**: 未解明
- **DESIGN PROPOSAL**: 事実ではなく本書の設計提案

---

## 0. 今回の新規解析で確定した事項（本書で初出のもの）

| # | 事項 | Evidence |
|---|---|---|
| N1 | `GamePadDuplicateWrite()`は**swapではなくunassign**。`cfgSetBit(g_srcidx, &config_data[4], g_chgkey, 1)`の後に`cfgSetBit(g_dstidx, &config_data[4], 0, 1)`を呼び、重複相手を`0`（未割当）にする。DATAFLOW §14.4の「swap」HYPOTHESISは**FALSIFIED** | CONFIRMED（raw disasm `0x18228ED8D`〜`0x18228EDFA`、2回目呼出の`r8d`は`xor r8d,r8d`=0） |
| N2 | `raw=0`は「未割当」。`GamePadDoneChk()`は`cfgGetBit(3..33)`が全て非0のときだけ`true`を返す | CONFIRMED（raw disasm `0x18228EC81`〜`0x18228ECCA`） |
| N3 | `ChangeKey(Type, ref dupflg)`は新しいボタンを引数で受け取らない。`InputUtil.GetOneShotKey(0)`（そのフレームで押された物理ボタン）を`dds3ConfigGamePadSteam.iumap`で引いてchgkeyを得る。**live入力依存でMODからは使えない** | CONFIRMED（Ghidra decompile + VA→method逆引きで`0x1821412B0`=`InputUtil.GetOneShotKey`） |
| N4 | `ChangeKey`末尾の`FUN_1822ee830(1)`は`cmpMisc.cmpPlaySE(1)`（効果音再生）。DATAFLOW §1.3で長くUNRESOLVEDだった呼出の正体 | CONFIRMED（metadata逆引き、`0x1822EE540`=`cmpMisc.cmpPlaySE2`） |
| N5 | 純正UIの重複処理フロー: `Updata`→`ChangeKey`が`dupflg=true`なら`MessageEnd()`→`MsgChoiceMsg(0xC8, …)`（確認ダイアログ種別200）。`EndChoiceUpdate`で種別`0xC8`かつ`EndChoicePos==0`のときだけ`GamePadDuplicateWrite()`を呼ぶ。それ以外（No）は何も書かない | CONFIRMED（分岐構造）/ STRONGLY SUPPORTED（`EndChoicePos==0`＝「はい」という意味付け） |
| N6 | 変更確定時の保存経路: `DateSave()`は2つのArray.Copyループの後、`FsSaveData.SetConfigLocal(tab, dds3GlobalWork.Instance.config_data[tab])`を6タブ分呼ぶ。`SetConfigLocal`は`FsSaveData.config_data[tab]`へ要素コピーし、末尾で`SteamConfigLocalSave()`へtail-jumpする。`SteamConfigLocalSave`は6タブを0x600byteのbufferへ詰め、encodeし、`SteamOptionFile.Save(buf, fnm)`へtail-jumpする | CONFIRMED（raw disasm、`DateSave`内`0x1822BAC8F`〜`0x1822BAE7C`の6 call、`SetConfigLocal`末尾`jmp 0x182707150`、`SteamConfigLocalSave`末尾`jmp 0x182601C50`） |
| N7 | `EndChoiceUpdate`の種別`0x190`（400）で「はい」なら`DateSave()`の後にもう一度`SteamConfigLocalSave()`を呼ぶ。「いいえ」なら`DatePrev`（`SelText[1]←SelText[0]`、編集破棄）。つまり**純正はConfig画面を抜けるときの一括保存確認**方式 | CONFIRMED（分岐構造）/ STRONGLY SUPPORTED（種別400＝「変更を保存しますか」という意味付け） |
| N8 | `DateSave`と`dds3FirstInit`が使うFsSaveDataインスタンスは同じもの（同一anchor `0x182E4EEF0`のstatic `+0x8`）。このanchorは`GlobalData`クラス（`public static FsSaveData saveData; //offset 0x8`）| CONFIRMED（anchor・offsetが同一）/ STRONGLY SUPPORTED（`GlobalData.saveData`というfield名への対応付け、metadataのfield順・型と一致） |
| N9 | `SteamConfigLocalCopy(tabpos=4, gamelvlflg=false)`（単一タブ）は`FsSaveData.config_data[4]`を`dds3GlobalWork.Instance.config_data[4]`・`SelText[0][4]`・`SelText[1][4]`の3箇所へArray.Copyする（`tabpos==1 && gamelvlflg`のときだけ特殊処理あり、tab4には無関係） | CONFIRMED（raw disasm `0x18270629A`〜`0x182706509`） |
| N10 | `ChangeKeyDuplicate`が使う`chg_padmap`（static `+0x60`、34要素）と`iumap`（`+0x38`）、`config_gamepad_priSet`（`+0x58`）は`dds3ConfigGamePadSteam..cctor`で生成される。Config画面の`Init()`には依存しない | CONFIRMED（cctor内の`mov [rcx+0x60], rbx`等）。runtime中に別経路で差し替わるかは未確認 |
| N11 | `g_chgkey/g_srcidx/g_dstidx`はcctorで明示初期化されず（既定値0）、`ChangeKeyDuplicate`の重複検出時にのみ書かれる。`GamePadDuplicateWrite`はこれらを**検証せず**使う | CONFIRMED |
| N12 | Type（`config_data[4]`の添字）とSettings/`GetConfigGamePad`のindexの関係は`Type = index + 3`。`ChangeKey`のswitchの区切り（3-9 / 10-19 / 20-22 / 23 / 24-28 / 29-33）がUIのcontext区切り（COMMON 0-6 / FIELD・DUNGEON 7-16 / BATTLE 17-19 / EVENT 20 / PUZZLE 21-25 / WARP FIELD 26-30）と完全に一致する | CONFIRMED（idx4..9のbyte-exact `innerIdx=idx+3`、DATAFLOW §17.3）/ STRONGLY SUPPORTED（全域への一般化、境界一致による） |
| N13 | `DateSave`はtab 4/5のとき`dds3ConfigMainSteam.ChangeTexEXE`（static `+0x1D9`）を`true`にする。名前からボタンガイドアイコンの再生成フラグと推定 | CONFIRMED（書込み命令）/ HYPOTHESIS（意味） |
| N14 | 呼出し可能性（metadataのアクセス修飾子）: `ChangeKeyDuplicate`=**private static**、`GamePadDuplicateWrite`/`GamePadDoneChk`/`cfgGetBit`/`cfgSetBit`/`GetConfigGamePad`/`DateSave`=public static、`FsSaveData.SetConfigLocal`/`GetConfigLocal`/`SteamConfigLocalCopy`/`SteamConfigLocalSave`=public instance、`DatePrev`=private static。**ただしIl2CppInteropのproxy DLL上では`ChangeKeyDuplicate`もpublic staticとして生成されており、Reflectionなしで直接呼べる**（§12.1） | CONFIRMED（metadata flags / proxy DLLのMethodDef flags） |

> **2026-09-24 追記**: §3・§5・§7の一部は§12（Pre-PoC Transaction Verification）で byte-exact に確定・訂正した。**PoC実装の正本は§12とする。**

---

## 1. Current known dataflow（現状整理）

### 1.1 5つの記憶領域

| 領域 | 実体 | 役割 | Evidence |
|---|---|---|---|
| A | `dds3GlobalWork.Instance.config_data[4]`（`Int32[]`） | 純正の「編集・作業用」値。`ChangeKey`/`ChangeKeyDuplicate`/`cfgSetBit`/`cfgGetBit`/`GamePadDoneChk`が読み書きする | CONFIRMED |
| B | `dds3ConfigMainSteam.SelText[0][4]` | 「live」値。`GetConfigGamePad`が読む（MODのread-only表示もここ） | CONFIRMED |
| C | `dds3ConfigMainSteam.SelText[1][4]` | 「編集バッファ」。Config画面を開くと領域Aへの参照aliasになる | CONFIRMED |
| D | `GlobalData.saveData.config_data[4]`（FsSaveDataのローカル） | 保存用のミラー。`SetConfigLocal`で更新、`SteamConfigLocalSave`でファイル化 | CONFIRMED（N6/N8） |
| E | option file（`SteamOptionFile.Save`の書出し先、`SteamSaveData.GetFilePath`で決まるpath） | 永続保存 | CONFIRMED（経路）/ UNRESOLVED（ファイル名・encode形式） |

### 1.2 経路図

```
[起動]
SteamOptionFile.LoadFile ─→ D  (FsSaveData.SteamConfigLocalLoad)
dds3FirstInit → SteamConfigLocalCopy(-1,false):  D ─→ A, B, C   (Array.Copy ×3, 全6タブ)

[純正Config画面]
dds3ConfigProcessStart:  C := A  (参照alias、全6タブ)
キー変更:  Updata → ChangeKey(Type) → (GetOneShotKey→iumap→chgkey)
            → ChangeKeyDuplicate → 重複なし: cfgSetBit → A[Type]=chgkey  (=C, alias)
                                 → 重複あり: g_*へ記録 → 確認ダイアログ(0xC8)
                                      はい → GamePadDuplicateWrite: A[src]=chgkey, A[dst]=0
                                      いいえ → 何もしない
画面を抜ける: cfgChkFlagChange (B≠C?) → 確認ダイアログ(0x190)
      はい → DateSave: A:=C, B:=C, SetConfigLocal(tab, A[tab]) → D:=A → SteamConfigLocalSave → E
              → さらに SteamConfigLocalSave → E
      いいえ → DatePrev: C:=B (aliasなのでAも元に戻る)
      ※ 未割当(0)が残る間はGamePadDoneChk=false（N2）。純正UIはこれで退出を止めると推定（STRONGLY SUPPORTED、dds3UpdateConfigからの呼出はCONFIRMED、止める挙動自体はユーザー観察「未割当のactionがあります」表示と整合）
```

---

## 2. Native API behavior（純正API表）

| method | VA / access | input | write target | duplicate behavior | SelText更新 | config_data(A)更新 | save | UI依存 |
|---|---|---|---|---|---|---|---|---|
| `ChangeKey(Type, ref dupflg)` | `0x18228E320` public static | Type。**新ボタンはそのフレームのlive入力**（`GetOneShotKey`→`iumap`） | A（`ChangeKeyDuplicate`経由。Type=0x17(23)のみ`cfgSetBit`直呼び） | `ChangeKeyDuplicate`へ委譲。**Type=23（EVENT「テキストの早送り」）は重複チェックなし** | なし（Config画面中はC=Aのaliasで間接反映） | あり | なし | **あり**（live入力、効果音`cmpPlaySE(1)`）→ **MODでは使用不可** |
| `ChangeKeyDuplicate(Type, st_idx, ed_idx, chgkey, ref dupflg)` | `0x18228DEA0` **private static** | Type, chgkey（raw値を明示） | 重複なし→`cfgSetBit(Type,&A,chgkey,1)`でA[Type]。重複あり→`g_chgkey/g_srcidx/g_dstidx`のみ | 最初に見つけた重複相手1件を記録し`dupflg=true`で**書かずに戻る** | なし | 重複なし時のみ | なし | **なし**（cctor由来の静的テーブル＋Aのみ、N10）。STRONGLY SUPPORTED |
| `GamePadDuplicateWrite()` | `0x18228ECE0` public static | `g_*`（引数なし） | A[g_srcidx]=g_chgkey、A[g_dstidx]=0 | **unassign**（N1） | なし | あり | なし | なし。ただし**`g_*`を検証しない**（N11） |
| `cfgSetBit(Type, ref pFlag, value, execflag)` | `0x182296EF0` public static | Type, 配列, 値 | pFlag[Type]。Type=0/1は**プリセット一括適用**、2はclamp | なし（汎用setter） | なし | pFlagがAなら | なし | なし。直接使うと重複処理を迂回するので**使わない** |
| `cfgGetBit(Type)` | `0x182296C90` public static | Type | なし（read） | — | — | — | — | なし。A[Type]を返す（Type 3..6は条件付きで定数、DATAFLOW §12.2のidx0..3と同じ特殊処理） |
| `GamePadDoneChk()` | `0x18228EC60` public static | なし | なし（read） | — | — | — | — | なし。A[3..33]に0が無ければtrue |
| `ChgConfigGamePadAll()` | `0x18228E9B0` public static | なし | **なし**（境界チェックのみ、DATAFLOW §11.3） | — | — | — | — | 使う意味なし |
| `ChgConfigGamePad(Type, value)` | `0x1810D6760` public static | ? | UNRESOLVED（GameObject探索主体の巨大関数、DATAFLOW §13.3） | ? | ? | ? | ? | **使用禁止**（未解明） |
| `DateSave()` | `0x1822BA990` public static | なし（B≠Cでないと即return） | A:=C, B:=C, D:=A, E | — | B更新 | C→A | あり（`SetConfigLocal`×6 → `SteamConfigLocalSave`×6） | Config画面が開いてC=Aのaliasがある前提。**画面外で呼ぶとCの古い値でAを上書きする危険**（loop1が先に走るため）→ **MODでは使わない** |
| `FsSaveData.SetConfigLocal(tab, cfg)` | `0x182706000` public instance | tab, `Int32[]` | D[tab]:=cfg（要素コピー）→ `SteamConfigLocalSave()` → E | — | なし | なし | **あり** | なし |
| `FsSaveData.SteamConfigLocalCopy(tab, gamelvlflg)` | `0x182706250` public instance | tab（-1で全タブ） | D[tab] → A[tab], B[tab], C[tab] | — | **B・C更新** | あり | なし | なし（起動時にも使われている経路） |
| `FsSaveData.GetConfigLocal(tab)` | `0x181C073F0` public instance | tab | なし（D[tab]の**参照**を返す） | — | — | — | — | なし |
| `FsSaveData.SteamConfigLocalSave()` | `0x182707150` public instance | なし | E（Dの6タブをencodeして保存） | — | — | — | あり（戻り値void、成否を返さない） | なし |

**`ChangeKeyDuplicate`の重複判定規則（CONFIRMED、Ghidra decompile。`st_idx`/`ed_idx`はdecompile上未使用＝STRONGLY SUPPORTED）:**

新しい値`chgkey`をType `t`へ割り当てるとき、`u = 0..33`（`u≠t`）を先頭から走査し、次を全て満たす最初の`u`を重複相手とする:

1. `chg_padmap[u][1] != -1`（編集対象のaction）
2. 例外ペアでない: `(8, 17)`・`(8, 28)`は互いに重複扱いしない
   - Type 8 = index 5「キャンセル」、Type 17 = index 14「視点を正面に戻す」、Type 28 = index 25 PUZZLE「視点切替」（N12の対応による、STRONGLY SUPPORTED）。既定値でキャンセルと視点を正面に戻すが両方Bなのと整合する
3. WARP FIELD（Type 29..33）同士、またはWARP FIELD以外同士である
4. カテゴリ条件: `chg_padmap[t][0]==0`、または`chg_padmap[u][0]`が`chg_padmap[t][0]`と同じか0
5. `cfgGetBit(u) == chgkey`

`chg_padmap[*][0]`（カテゴリ）と`[*][1]`の実値は未ダンプ（UNRESOLVED）。既定値でコマンドメニュー（FIELD）とオートバトル（BATTLE）が両方Yで共存しているため、contextが違えば重複扱いしない設計と推定（STRONGLY SUPPORTED）。

---

## 3. Proposed edit transaction（DESIGN PROPOSAL）

### 3.1 推奨API

**結論: 純正UIの`ChangeKey`は使わず、`ChangeKeyDuplicate`（private、Reflection）を重複判定と書込みの本体として使う。その後の同期と保存は`FsSaveData`のpublic API（`SetConfigLocal` → `SteamConfigLocalCopy`）で行う。`DateSave`は使わない。**

理由:
- `ChangeKey`はlive入力依存（N3）なので、MODから任意のボタンを指定できない。
- `ChangeKeyDuplicate`は`ChangeKey`の中核で、chgkeyを引数で受け取り、純正の重複規則（§2）をそのまま適用する。静的テーブルとAにしか依存しない（N10）。
- `DateSave`はConfig画面のaliasを前提にしており、画面外ではloop1（A:=C）がMODの変更を消す。
- `SetConfigLocal(4, A[4])`は`DateSave`がtab4に対して行う保存処理そのもの（N6）。
- `SteamConfigLocalCopy(4, false)`は起動時にB・Cを作る処理そのもの（N9）。D→A/B/Cの向きなので、保存済みの値からlive値を作り直すことになる。「保存されていない値がliveになる」状態が構造的に起きない。

API優先順位（指示の1〜4）との対応:

| 手順 | API | 優先度 |
|---|---|---|
| 重複判定・書込み | `ChangeKeyDuplicate` | 1（native上はprivateだがproxy上public、§12.1） |
| 重複時のunassign（v1では使わない） | `GamePadDuplicateWrite` | 1 |
| 保存 | `GlobalData.saveData.SetConfigLocal` | 1 |
| live反映 | `GlobalData.saveData.SteamConfigLocalCopy` | 1 |
| 検証 | `GetConfigGamePad`, `cfgGetBit`, `GamePadDoneChk`, `GetConfigLocal` | 1 |
| rollback | `SetConfigLocal`（snapshot配列）→ `SteamConfigLocalCopy` | 1 |

**直接のarray/memory writeは一切使わない。** 必要なのは値のreadだけ（`dds3GlobalWork.Instance.config_data[4]`を`Int32[][]`として読む。metadataの型とnative offset 0xE8が一致しておりread-onlyで安全。`SelText`はmetadataの型注釈`String[][]`が実体と矛盾するため**Il2CppInterop経由で絶対に触らない**、DATAFLOW §12.2）。

### 3.2 Apply transaction（1 action変更、DESIGN PROPOSAL）

Settings（別プロセスのWinForms）はGAME bindingを直接変えられないので、既存の`feature-requests.json`と同じ要求ファイル方式を使う。Core DLLがゲームのメインスレッドで処理する。

```
Settings: NocturneModernController.game-binding-requests.json に要求を書く
   { requestId, index(0..30), expectedCurrentRaw, newRaw }
      ↓
Core (OnUpdate、メインスレッド、1フレーム内で完結):

 T0 前提チェック（どれか1つでも不成立なら「実行せずreject」）
    - readiness（§9）を満たす
    - index が編集許可リスト（§8、v1はCONFIRMED 15件）にある
    - newRaw が許可raw値（§8）
    - GetConfigGamePad(index) == expectedCurrentRaw   ← Settingsが古い表示のまま出した要求を弾く
    - newRaw != 現在値（no-opは成功扱いで終了）
    - GamePadDoneChk() == true（元から未割当がある異常状態では触らない）

 T1 snapshot（§7）
    S_A  = A[4] の全要素コピー（配列長ぶん）
    S_D  = GetConfigLocal(4) の全要素コピー
    S_34 = GetConfigGamePad(0..33)
    S_A と S_D が不一致なら reject（保存されていない変更が既にある）

 T2 重複判定＋書込み
    Type = index + 3
    ChangeKeyDuplicate(Type, 0, 0, newRaw, out dup)
      ├ dup == true  → A は変わっていない（N5/N1）。g_srcidx/g_dstidx/g_chgkeyを読んで
      │                「index X と重複」としてSettingsへ返し、終了（v1はreject、§4）
      └ dup == false → A[Type] == newRaw のはず

 T3 ChangeKeyDuplicateが（重複なしと判定して）内部でcfgSetBitを呼び、書込みを終えた後の検証（保存前）
    ※ 旧表現「ChangeKeyDuplicateで書いた直後」の意味をここで明確化。
      書込み本体はcfgSetBitで、ChangeKeyDuplicateは「重複検査＋重複なし時にcfgSetBitを呼ぶ」関数（§12.1）
    - cfgGetBit(Type) == newRaw
    - A の Type 以外の全要素が S_A と一致（想定外の書込みが無い）
    - GamePadDoneChk() == true
    不成立 → rollback R1（§7）

 T4 保存
    saveData.SetConfigLocal(4, A[4])      ← D:=A、SteamConfigLocalSave → E
    検証: GetConfigLocal(4) の全要素が A[4] と一致
    不成立 → rollback R2

 T5 live反映
    saveData.SteamConfigLocalCopy(4, false)   ← D → A, B, C
    検証: GetConfigGamePad(index) == newRaw、他の33件は S_34 と一致
    不成立 → rollback R2

 T6 結果
    - MODの既存snapshot（actions.json内のGameActionBindings）を更新
    - game-binding-requests.json を削除し、結果を書く（success / reject理由 / rollback有無）
```

T2〜T5は同じフレーム・同じスレッドで連続実行するので、ゲーム側コードが途中の状態を見ることはない（STRONGLY SUPPORTED、IL2CPP側はシングルスレッドの更新ループが前提。T2〜T5の間にyieldしない実装にする）。

### 3.3 `config_data` / `SelText[0]` / `SelText[1]` の整合方法

- **MODは3領域のどれにも直接書かない。**
- A（config_data[4]）は`ChangeKeyDuplicate`→`cfgSetBit`が書く（純正）。
- D（保存ミラー）は`SetConfigLocal`が書く（純正、`DateSave`がtab4でやるのと同じ処理）。
- A・B（SelText[0]）・C（SelText[1]）の最終値は`SteamConfigLocalCopy(4,false)`がDから作り直す（純正、起動時と同じ処理）。
- これで`A == B == C == D`（tab4）になり、純正Config画面を後で開いても`cfgChkFlagChange`（B≠C?）は「変更なし」と判定する。保存確認ダイアログが不意に出ることも、画面で「いいえ」を選んでMODの変更が戻ることもない（STRONGLY SUPPORTED、DATAFLOW §18.2の`cfgChkFlagChange`仕様による）。
- 一度Config画面を開いたあとはC=Aのaliasが残っている可能性がある（UNRESOLVED）。その場合でも`SteamConfigLocalCopy`は同じ配列へ2回同じ値をコピーするだけなので、結果は変わらない（CONFIRMED、Array.Copyの性質）。

---

## 4. Duplicate handling

### 4.1 純正UIの挙動（事実）

| 選択肢 | 純正の挙動 | Evidence |
|---|---|---|
| swap | **しない** | CONFIRMED（N1。swap説はFALSIFIED） |
| unassign | 確認ダイアログで「はい」なら、新しいボタンを割当て、**重複相手を0（未割当）にする** | CONFIRMED（N1・N5）＋実機観察（INDEX_MAPバッチ1で「PUZZLEメニュー」が空欄化） |
| reject | 「いいえ」なら何も書かない | CONFIRMED（分岐構造）/ STRONGLY SUPPORTED（Yes/Noの意味付け） |
| confirmation | ダイアログあり（`MsgChoiceMsg(0xC8)`） | CONFIRMED |
| 未割当の後始末 | 未割当が残る間は`GamePadDoneChk=false`。ユーザーが空欄に手動で割り当てる必要がある | CONFIRMED（関数仕様）/ STRONGLY SUPPORTED（画面退出を止める挙動） |

### 4.2 MOD側方針（DESIGN PROPOSAL）

- **v1: reject only。** 重複したら何も書かずにSettingsへ「〈相手action名〉と重複しています」と返す。ユーザーは相手を先に別ボタンへ移してから再試行する。
  - 理由: 純正の「はい」はunassignで、そのあと未割当を埋める手順が必要になる。MODから1回のApplyで未割当を作ると、`GamePadDoneChk=false`の状態が保存されてしまう。純正UIではその状態で画面を抜けられないので、純正が想定していない保存状態になる。
- **v2候補: 純正と同じunassign。** `GamePadDuplicateWrite`を呼び、相手を空欄にする。ただし保存（T4）は、未割当が0件（`GamePadDoneChk=true`）になってからのバッチApplyに限る。
- **v3候補: MOD独自のswap。** 純正に無い挙動なので、2回の`ChangeKeyDuplicate`で組み立てることになる。一時的な未割当や重複を経由するため設計が複雑。優先度は低い。

### 4.3 `GamePadDuplicateWrite`を使う場合の必須ガード（v2以降、DESIGN PROPOSAL）

N11のとおり`g_*`は検証されない。古い値や既定値0のまま呼ぶと`cfgSetBit(0, …)`になり、**プリセット0がGAMEPAD全体へ一括適用される**（DATAFLOW §13.3、`cfgSetBit`のcase 0/1）。そのため:

1. 同じフレーム内で自分が呼んだ`ChangeKeyDuplicate`が`dupflg=true`を返した直後にだけ呼ぶ。
2. 呼ぶ前にstatic fieldを読み、`g_srcidx==Type`・`g_chgkey==newRaw`・`3 <= g_dstidx <= 33`・`g_dstidx != Type`を確認する（int fieldのreadのみ）。
3. どれか1つでも合わなければ呼ばない。

---

## 5. Save / persistence

- **CONFIRMED**: 純正の永続化は`SetConfigLocal(tab, A[tab])` → `SteamConfigLocalSave()` → `SteamOptionFile.Save(buf, fnm)`。`DateSave`はこれを6タブ分呼び、`EndChoiceUpdate`はさらに`SteamConfigLocalSave()`を1回呼ぶ（N6・N7）。
- **DESIGN PROPOSAL**: MODはtab4だけ`SetConfigLocal(4, A[4])`を1回呼ぶ。Dの他タブは変えないので、`SteamConfigLocalSave`は他タブを現状の値のまま書き直すだけになる（CONFIRMED、6タブ全体をDから詰める仕様）。
- **「runtimeだけ変わって再起動で戻る」状態を防ぐ仕組み**: T5のlive反映はDからのコピーなので、T4（D更新）が済んでいなければliveも変わらない。runtimeだけ新しい値になる順序が存在しない。
- **弱点（UNRESOLVED）**: `SteamConfigLocalSave`/`SteamOptionFile.Save`は成否を返さない（void）。T4の検証はメモリ上のDまでしか確認できず、ディスク書込みの成功は確認できない。
  - 対策（DESIGN PROPOSAL）: (a) 保存前後で保存ファイルのmtimeとサイズを読んで更新を確認する（ファイルパスの特定が前提、UNRESOLVED）。(b) 再起動後readback（§6.2）をPoCの必須項目にする。
  - `SteamOptionFile.LoadFile`を検証目的で呼んではいけない。読込み失敗時の分岐で`ConfigAllDateInit2`（config_dataの既定値初期化）を呼ぶ（DATAFLOW §16.2）。
- **一括保存か即時保存か**: 純正はConfig画面を抜けるときの一括保存（確認ダイアログ0x190）。MODは**Settings側で変更を溜め、Applyで一括送信する**方式にする（§8.3）。Core側の1 Applyは「全変更を適用 → 1回だけ`SetConfigLocal(4, …)`」にし、ファイル書込みは1 Applyにつき1回にする。

---

## 6. Readback verification

### 6.1 同一セッション内（T3〜T5）

| 段階 | 読むもの | 期待 |
|---|---|---|
| T3 | `cfgGetBit(Type)`、A全要素、`GamePadDoneChk()` | 対象だけ変化、他は不変、未割当なし |
| T4 | `GetConfigLocal(4)`全要素 | A[4]と完全一致 |
| T5 | `GetConfigGamePad(0..33)` | 対象indexだけnewRaw、他33件はS_34と一致 |

### 6.2 再起動後（PoC必須、DESIGN PROPOSAL）

- ゲームを完全終了して再起動し、exploration activeに入った後の`GetConfigGamePad(0..33)`（既存のauthoritative snapshot）が、Apply後の値と一致すること。
- 純正Controller Key Config画面を開いて、表示が変更後の値になっていること。何も変えずに閉じたとき保存確認ダイアログが出ないこと（B==Cの確認）。
- 実際にそのボタンでactionが発動すること。`GetConfigGamePad`の値とゲーム操作の対応はSTEP3で純正変更については実測済み（DATAFLOW §19.2）。ただしMOD経路での変更はまだ実測していない（UNRESOLVED）。

---

## 7. Rollback

### 7.1 snapshot（DESIGN PROPOSAL）

「34 raw値」（`GetConfigGamePad(0..33)`）だけでは**復元に足りない**。
- `GetConfigGamePad`のidx 0..3は条件付きで定数を返し、31..33は常に0を返す。A/Dの実データとは1対1にならない（DATAFLOW §12.2・N12）。
- A[4][0..2]（プリセット番号等）は`GetConfigGamePad`では見えない。

したがってsnapshotは3種類持つ:
- `S_A`: A[4]の全要素（配列長ぶん）… 復元の正本
- `S_D`: D[4]の全要素 … 保存状態の確認用
- `S_34`: `GetConfigGamePad(0..33)` … live検証用

Core側でメモリ上に保持し、ディスクにも`game-binding-rollback.json`として書く（異常終了時用）。

### 7.2 復元手順（純正APIのみ、DESIGN PROPOSAL）

| ID | いつ | 手順 |
|---|---|---|
| R1 | T3で失敗（Aだけが変わり、D・Eは未変更） | `SteamConfigLocalCopy(4,false)`のみ。D（==S_D）からA/B/Cを戻す。ファイル書込みなし |
| R2 | T4/T5で失敗（D・Eも変わった可能性） | `S_A`から新しい`Int32[]`を作り、`SetConfigLocal(4, S_A配列)` → `SteamConfigLocalCopy(4,false)`。DとE（ファイル）を元値で書き直し、A/B/CはDから戻る |
| R3 | R1/R2後の検証 | `GetConfigLocal(4)==S_A`、`GetConfigGamePad(0..33)==S_34`。失敗したら以後の編集を無効化し、Settingsに「手動確認が必要」と表示する。ゲーム側への追加操作はしない |

- R1/R2は「値を元の配列内容に戻す」だけで、純正APIの`SetConfigLocal`/`SteamConfigLocalCopy`しか使わない。直接memory writeは使わない。
- R1が`SteamConfigLocalCopy`だけで済むのは、T1で`S_A==S_D`を確認済みで、T4に進んでいないのでD==S_Dのままだから。保存を変えない、最小の復元になる。
- **Settings側が異常終了した場合**: Core側の処理は要求ファイル1件単位で完結するので、Settingsの状態に影響されない。溜めていた未送信の変更はSettings側の破棄扱い（純正の「いいえ」と同じ）。
- **ゲームが異常終了した場合**（T4〜T5の途中）: 起動時の`dds3FirstInit`がEからDとA/B/Cを作り直すので、runtimeとファイルの不整合は残らない。残る可能性があるのは「ファイルは新しい値・MODの結果ファイルは未記録」だけ。起動時に`game-binding-rollback.json`が残っていたら、S_34と現在値を比べてSettingsに表示する（自動復元はしない）。

---

## 8. Settings UX（DESIGN PROPOSAL）

### 8.1 対象action

| 区分 | v1 | 理由 |
|---|---|---|
| CONFIRMED 15件（index 4,5,6,7,12,13,14,15,16,17,18,19,20,23,30） | **編集可** | index↔action対応をA/B/Aで実測済み |
| STRONGLY SUPPORTED（0-3, 8-11, 21-22, 24-29） | 表示のみ（「未検証」ラベル）、編集不可 | index対応が構造仮説のみ。0-3（移動）は`cfgGetBit`/`GetConfigGamePad`が定数を返す特殊処理あり。8-11（視点変更 上下左右）は純正UIでグレーアウト |
| 31-33 | 表示しない | 画面外、常に0 |

### 8.2 ボタン選択肢

- 新ボタンのdropdownは、**INDEX_MAPで実測済みのraw値だけ**にする: `A=10, B=9, Y=11, X=12, LB=13, LT=14, RB=15, RT=16, L3=17, R3=18, SELECT=25, START=26`。
- 十字キー・スティック方向（raw未確認）とraw 0（未割当）は選択肢に出さない。
- `iumap`（物理ボタン→raw）の全表をcctorから静的抽出すれば選択肢を増やせる（未実施、UNRESOLVED）。

### 8.3 操作フロー

```
[GAMEキーコンフィグ]タブ
  Action | Context | 現在のボタン | 新しいボタン(dropdown) | 状態
  ...
  [適用] [取消] [再読込]
```

- 変更はSettings側で溜める（行を黄色表示）。**適用**で1要求にまとめて送る。**取消**で溜めた変更を破棄する（純正の保存確認「いいえ」に相当）。
- 同じApply内の重複は、Settings側で事前チェックして送る前に警告する（純正規則の近似として、同じcontext内の同一ボタンを警告）。最終判定はCore側の`ChangeKeyDuplicate`が行う。
- 重複でrejectされたら、その行に「〈相手action〉と重複」と表示する。ボタンの入替えは、ユーザーが相手の行も同時に変更して再Applyする（v1はswapを自動化しない）。
- 複数変更のApplyはCore側で順番に`ChangeKeyDuplicate`を呼ぶ。「A→X、B→A」のような順序依存の入替えは、途中の状態で重複になりうる。v1では**1 Apply = 1 action**に制限して、この問題を避ける。
- **Reset/Default**: v1なし。純正の`DefaultPrev`（UI経由、`SteamConfigLocalCopy`を呼ぶ）の再利用は後段。
- **プリセットA/B/C**: v1なし。`cfgSetBit(0/1)`は34要素を一括で上書きするので、v1の検証範囲を超える。
- 結果表示: 成功 / reject（理由）/ rollback実行、の3種類。

---

## 9. Safety constraints（DESIGN PROPOSAL）

1. **readiness**: `FieldDashPatch.IsExplorationActive == true` **かつ** authoritative snapshot取得済み（既存の`_gameActionBindingsReady`）のときだけ編集要求を処理する。未達なら要求を保留し、「フィールド探索中に適用されます」と表示する。readinessの判定基準自体は変えない。
2. **純正Config画面との排他**: Config画面が開いている間はC=Aのaliasがあり、`DateSave`/`DatePrev`と競合する。「Config画面が開いている」を直接検出する安全なsignalは無い（UNRESOLVED、DATAFLOW §20.5）。exploration active中は純正Config画面が開いていない、という前提に頼る（STRONGLY SUPPORTED：診断ログでメニュー／Config操作中は`exploration=False`、DATAFLOW §19.1）。
3. タイトル画面からの編集は後段（`GlobalData.saveData`とAが揃うのは`dds3FirstInit`の後。タイトルでの安全性は未検証）。
4. 呼んではいけないAPI: `ChangeKey`（live入力）、`DateSave`/`DatePrev`（画面前提）、`cfgSetBit`の直接呼出し（重複規則を迂回。Type 0/1ならプリセット一括上書き）、`ChgConfigGamePad`（未解明）、`SteamOptionFile.LoadFile`（副作用あり）、`GamePadDuplicateWrite`のガードなし呼出し。
5. `SelText`にはIl2CppInterop経由で触らない（型注釈が実体と矛盾）。readは`GetConfigGamePad`経由に限る。
6. Type範囲は3..33に限る。0..2はプリセット系なので、MODのコードパスが0..2を渡すことが構造的に起きないようにする（index→Type変換の入口で拒否）。
7. 1フレーム1要求。T0〜T6は同じフレームで完結させ、途中でyieldしない。
8. 例外は全てcatchしてrollbackへ進める。Reflectionで`ChangeKeyDuplicate`が見つからない場合（ゲーム更新時など）は機能ごと無効化する。
9. 起動時にGameAssembly.dllのSHA-256がcanonical値と違ったら、編集機能を無効化する（VA・private methodに依存するため）。

---

## 10. Implementation phases（DESIGN PROPOSAL）

| Phase | 内容 | 成功条件 |
|---|---|---|
| P0（今回） | 本設計書 | — |
| P1 静的補強（read-only） | `chg_padmap`・`iumap`・`config_gamepad_priSet`の中身をcctorから静的抽出。`ChangeKeyDuplicate`の`st_idx/ed_idx`未使用をraw disasmで確認。保存ファイルのpathを特定（`SteamSaveData.GetFilePath`） | 重複規則の全表と、raw↔物理ボタンの全表ができる |
| P2 安全PoC（1 action変更→元に戻す） | 下の§10.1 | 同一セッションと再起動後の両方でreadbackが一致し、元に戻せる |
| P3 Core transaction実装 | §3.2のT0〜T6、§7のrollback、要求／結果ファイル。v1はCONFIRMED 15件・1 Apply=1 action・重複はreject | P2と同じ検証を自動で行う |
| P4 Settings UI | §8 | 実機でApply/取消/reject表示を確認 |
| P5 拡張 | 重複時unassign（ガード付き）、複数action、Default、プリセット、STRONGLY SUPPORTED actionの追加 | 個別にA/B/A |
| cleanup | `GameBindingDiagnosticProbe.cs`と`ModMain.cs`の`Sample()`呼出しを撤去するcommitを、P3実装とは**別のcommit**にする | — |

### 10.1 次に必要な安全PoC（DESIGN PROPOSAL）

**対象**: index 7「コマンドメニュー」（Type 10）。現在Y(11)。何度もA/B/Aした実績があり、変更先X(12)はFIELD/DUNGEON内で未使用なので重複しない（INDEX_MAP #7でX割当の実績あり）。

**手順**（実機、ユーザー立会い。1回の起動で1往復）:
1. 保存ファイルのバックアップ（P1でpath特定後、ゲーム停止中にユーザーがコピー）。
2. 起動 → exploration active → S_A/S_D/S_34を取得してログ出力。
3. 一時的なデバッグトリガー（キー入力など）で`ChangeKeyDuplicate(10, 0, 0, 12, out dup)`を1回だけ呼ぶ → T3検証 → `SetConfigLocal(4, A[4])` → T4検証 → `SteamConfigLocalCopy(4,false)` → T5検証。各段階の値をログ出力する。
4. ゲーム内で、XでコマンドメニューがXで開くこと・Yで開かないことを確認。
5. 純正Config画面を開いて表示がXであること、何も変えずに閉じて保存確認が出ないことを確認。
6. 完全終了 → 再起動 → exploration activeでreadbackがXであることを確認（永続化の確認）。
7. 同じトリガーでY(11)へ戻す → 再起動 → Yを確認（A/B/A）。
8. rollback経路の確認: 意図的に失敗させた条件（例: `expectedCurrentRaw`を不一致にしてT0で拒否。T3検証の閾値を一時的に不成立にしてR1を実行）で、値が変わらないことを確認。

**PoCで禁止すること**: 重複を起こす変更、`GamePadDuplicateWrite`の呼出し、index 0..3・8..11・21..33の変更、プリセット操作、複数actionの同時変更。

---

## 11. UNRESOLVED一覧

1. ~~保存ファイルのパス・encode形式。ディスク書込み成否の確認手段~~ → **§12.3で解決**（`%APPDATA%\SEGA\smt3hd\<steamid>\SMT3HDCONFIG`、ASCIIテキスト。ファイルを読み戻して確認できる）。
2. `chg_padmap[*][0]/[1]`の実値（重複判定のカテゴリと編集可否フラグの全表）。
3. `iumap`の全表（物理ボタン↔rawの完全対応）。十字キー・スティック方向のraw値。
4. MOD経路（`SteamConfigLocalCopy`によるB更新）の後に、ゲームの実入力が即座に新しい割当へ追従するか。純正変更でのGetConfigGamePad追従は実測済み。
5. `ChangeTexEXE`（N13）を立てないと、ボタンガイドのアイコンが古いまま残るか。残る場合、このboolを立てる手段（private static fieldへのReflection write＝優先度2だが「直接write」寄り）を採用するかは別途判断。
6. 純正Config画面を一度開いた後、C=Aのaliasがいつまで残るか（`dds3DestroyConfig`で解除されるか）。
7. 「Config画面が開いている」を検出する安全なsignal。
8. ~~`ChangeKeyDuplicate`の`st_idx/ed_idx`が本当に未使用か~~ → **§12.1でCONFIRMED（未使用）**。
9. `GamePadDoneChk=false`のとき純正が画面退出を止める具体的な挙動（`dds3UpdateConfig 0x1822CB616`周辺は未読）。
10. `ChgConfigGamePad(Type,value)`（`0x1810D6760`）の実体。

---

## 12. Pre-PoC Transaction Verification（2026-09-24 続き、ANALYSIS ONLY）

目的: PoC前に「1 action変更 → 永続保存 → runtime反映」で呼ぶ純正APIの順序と副作用をbyte-exactで固定する。**今回もnative呼出・write・build・deploy・commitは行っていない。** 使ったのはraw disassembly（capstone）、`global-metadata.dat`、Il2CppInterop proxy DLL（`MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll`）のmetadata読取り、`SMT3HDCONFIG`のread-only表示だけ。呼出先の名前は、全assemblyの`methodPointers`から作った逆引き表で解決した（mscorlib等のBCLを含む）。

### 12.1 書込み系4関数（byte-exact）

| method | proxy signature（Il2CppInterop） | return | `config_data[4]`へのwrite | 重複なし時 | 重複あり時 | `g_*`副作用 |
|---|---|---|---|---|---|---|
| `ChangeKey` `0x18228E320` | `public static bool ChangeKey(uint Type, ref bool dupflg)` | そのフレームに`iumap`と一致する押下が無ければ**false**（`0x18228E849 xor al,al`）。処理した場合はtrue | 間接（下の2関数経由） | Type 3..9/10..19/20..22/24..28/29..33 → `ChangeKeyDuplicate(Type, st, ed, chgkey)`。Type 23 → `cfgSetBit(23, &A, chgkey, 1)`（重複チェックなし） | `ChangeKeyDuplicate`に委譲 | 委譲先による |
| `ChangeKeyDuplicate` `0x18228DEA0` | `public static void ChangeKeyDuplicate(uint Type, int st_idx, int ed_idx, int chgkey, ref bool dupflg)` | void（結果は`dupflg`） | **あり（重複なし時のみ）** | 先頭で`dupflg=false`。34件走査して重複が無ければ `cfgSetBit(Type, &config_data[4], chgkey, execflag=1)`（`0x18228E267`〜`0x18228E27C`: `ecx=edi(Type)`, `rdx=config_data+0x40`, `r8d=r15d(chgkey)`, `r9b=1`）。その後return | **書込みなし**。`g_chgkey=chgkey`（`0x18228E21E`）、`g_srcidx=Type`（`0x18228E230`）、`g_dstidx=u`（`0x18228E241`）、`dupflg=true`（`0x18228E244`）の後にreturn | 重複時のみ3つとも上書き |
| `cfgSetBit` `0x182296EF0` | `public static …(uint Type, ref Int32[] pFlag, int value, bool execflag)` | 0/1（意味は使わない） | pFlagがAならあり | jump tableをdecodeした結果（CONFIRMED）: Type **0,1** → `0x182296F55`（clamp＋プリセット一括適用）、Type **2** → `0x182296FF3`（`pFlag[2]=clamp`）、Type **3..33** → `0x182297033`（`pFlag[Type]=value`、`0x182297040 mov [rcx+rdi*4+0x20], ebx`の1命令のみ）、34以上 → 何もしない | 重複判定をしない | なし |
| `GamePadDuplicateWrite` `0x18228ECE0` | `public static void GamePadDuplicateWrite()` | void | あり | — | `cfgSetBit(g_srcidx, &A, g_chgkey, 1)` → `cfgSetBit(g_dstidx, &A, 0, 1)`（unassign） | 読むだけ（検証なし） |

補足（CONFIRMED）:
- `ChangeKeyDuplicate`の`st_idx`(rdx)と`ed_idx`(r8)は、関数先頭で保存されず、`0x18228DF0F`と`0x18228DF2A`で別の値に上書きされる。**完全に未使用**。
- `ChangeKeyDuplicate`は名前に反して「重複検査のみ」ではない。**「重複検査を行い、重複が無ければ`cfgSetBit`で1要素を書く」atomicなcheck-and-write**。重複ありの場合だけ、書かずにpending state（`g_*`）を作る。
- 純正UIの単一変更は `ChangeKey` → `ChangeKeyDuplicate` → `cfgSetBit` の3段で、実際のstore命令は`cfgSetBit`の1命令だけ。`ChangeKey`が追加で行うのは「live入力からchgkeyを決める」ことと「効果音（`cmpPlaySE(1)`）」だけ。
- Il2CppInteropはnative上privateの`ChangeKeyDuplicate`もproxyではpublicとして生成している（MethodDef flagsでCONFIRMED）。Reflectionは不要。

**結論: 重複のないsingle rebindでMODが呼ぶentry pointは `dds3ConfigGamePadSteam.ChangeKeyDuplicate((uint)(index+3), 0, 0, newRaw, ref dup)` の1つ。**
- `ChangeKey`は不採用。新ボタンを引数で受け取れず、呼んだフレームの押下状態で結果が変わる。押下が無ければ何もせずfalseを返す（UI state依存、CONFIRMED）。
- `cfgSetBit`の直接呼出しも不採用。重複規則を迂回するうえ、Type 0/1を渡すとプリセット一括上書きになる。
- `ChangeKeyDuplicate`を呼ぶと、重複ありなら「何も書かない」、重複なしなら「Type 1要素だけ書く」のどちらかになり、中間状態は無い（CONFIRMED）。

### 12.2 SetConfigLocal / SteamConfigLocalCopy（byte-exact）

**`FsSaveData.SetConfigLocal`** `0x182706000` — `public void SetConfigLocal(int tabpos, Int32[] cfg)`（instance、return void）
- source: 引数`cfg`（要素を`i < cfg.Length`まで読む）
- destination: `this.config_data[tabpos][i]`（`this+0x20`、要素コピー。`0x18270605A mov [rdx+r8*4+0x20], eax`）
- 末尾: `jmp SteamConfigLocalSave`（`0x182706070`、tail call）→ **ディスク書込みまで行う**
- ループの上限は`cfg.Length`。コピー先の範囲チェックに失敗すると例外になる（`0x18270604D`）。そのため`cfg`の長さはコピー先以下（=256）でなければならない。

**`FsSaveData.SteamConfigLocalSave`** `0x182707150` — `public void SteamConfigLocalSave()`
- 副作用: `this.config_data[2][255] = this.field@0x28`（`0x1827071C5`。GAMEタブの予約スロット。純正の保存でも毎回行う処理）
- `this.config_data[0..5]`（各`Int32[256]`）を`Int32[0x600]`へ`Array.Copy`し、`Buffer.BlockCopy`で`byte[0x1800]`にする
- `SteamOptionFile.Save(bytes, fnm)`へtail jump（`0x1827073BA`）

**`SteamOptionFile.Save`** `0x182601C50`（BCL呼出しを全assembly逆引きで解決）
- `path = SteamSaveData.GetFilePath() + <定数>`。無ければ`Directory.CreateDirectory`
- `new StreamWriter(path + fnm, append=false, Encoding.ASCII)` → **テキストファイルを全体上書き**
- 1行目にheader、各タブで`"[" + タブ名 + "]"`、`Enum.GetNames(タブ別enum)`の**2番目から最後の1つ手前まで**を`String.Format(fmt, name, value[tab][i])`で1行ずつ出力（vtable `+0x290`の仮想呼出し。`TextWriter.WriteLine`と推定）
- try/finallyで`Dispose`。finally後に保留中の例外があれば再送出する（`0x1826021BB`〜`0x182602275`）。そのため**IO例外は`SetConfigLocal`の呼出し元まで伝播する**（STRONGLY SUPPORTED、制御フローから。実際の例外は未観測）

**`FsSaveData.SteamConfigLocalCopy`** `0x182706250` — `public void SteamConfigLocalCopy(int tabpos, bool gamelvlflg)`
- source: `this.config_data[tabpos]`（= FsSaveDataのD）。CONFIRMED
- destination（各`Array.Copy`、長さ0x100）: `dds3GlobalWork.Instance.config_data[tabpos]`（`0x1827063DD`）→ `SelText[0][tabpos]`（`0x182706474`）→ `SelText[1][tabpos]`（`0x1827064EB`）。CONFIRMED
- `tabpos=4`だけを指定して呼べる（`-1`は全タブループへ分岐、`0x182706294`）。CONFIRMED
- `gamelvlflg`: `tabpos==1 && gamelvlflg`のときだけ、コピー前に`GlobalWork.config_data[1][1]`を`D[1][1]`へ退避する（GAMEタブの難易度保護）。**tab4では無関係、falseでよい**。CONFIRMED
- 保存はしない（ディスク書込みなし）

### 12.3 永続化の4層（確定）

| 層 | 実体 | 書くAPI | Evidence |
|---|---|---|---|
| A. working/runtime | `dds3GlobalWork.Instance.config_data[4]`（A）、`SelText[0][4]`（B）、`SelText[1][4]`（C） | A: `ChangeKeyDuplicate`→`cfgSetBit`。A/B/C: `SteamConfigLocalCopy(4,false)` | CONFIRMED |
| B. FsSaveData local | `GlobalData.saveData.config_data[4]`（D、`Int32[256]`） | `SetConfigLocal(4, cfg)`（前半） | CONFIRMED |
| C. serialization | `Int32[0x600]` → `byte[0x1800]` → enum名 = 値 のテキスト化 | `SteamConfigLocalSave()` → `SteamOptionFile.Save` | CONFIRMED |
| D. physical file | `%APPDATA%\SEGA\smt3hd\870942052\SMT3HDCONFIG`（ASCIIテキスト、2975 bytes、最終更新 2026-09-23 22:34:11） | `StreamWriter`（`Save`内） | CONFIRMED（read-onlyで実ファイルを確認。最終更新時刻は§19 STEP3でYに戻して確定した時刻22:34:11と一致） |

**`SetConfigLocal`を1回呼べばB→C→Dまで同期的に完了する（CONFIRMED）。** 別の保存API（`SteamConfigLocalSave`の追加呼出しなど）は不要。純正の`EndChoiceUpdate`はさらにもう1回`SteamConfigLocalSave`を呼ぶが、Dが同じなので同じ内容を書き直すだけ。

**実ファイルから新たに確定したこと:**
- `[GAMEPAD]`セクションの行順は `PRESET`(1), `ANALOG_SENSITIVITY_ADJUSTMENT`(2), `MOVEMENT_FRONT`(3) … `COMMAND_MENU`(10) … `WF_PUNCH`(33)。これで **`Type = index + 3` が全33要素でCONFIRMED**（N12を格上げ）。
- 値もINDEX_MAPのCONFIRMED表と一致する（`DECISION=10`, `CANCEL=9`, `UIDISP=17`, `COMMAND_MENU=11`, `SUBJECTIVITY=18`, `AUTO_MAP=26`, `SKILL_HELP=25`, `NEXT_TURN=15`, `MENU=12`, `WF_PUNCH=9`等）。
- 保存されるのはenum名があるindex 1..33だけ。`config_data[4][0]`と`[34..255]`はファイルに出ない（CONFIRMED、`Save`のループ範囲）。
- フォルダに`steam_autocloud.vdf`がある＝Steam Cloud同期の対象。純正の保存と同じファイルなので扱いは同じ（クラウドとの競合時の挙動はUNRESOLVED）。
- **ディスクへの書込みは、このファイルを読んで検証できる**（§6の弱点を解消）。`[GAMEPAD]`の`<KEY>=<raw>`行をparseすればよい。検証のために`SteamOptionFile.LoadFile`を呼ぶ必要はない（呼んではいけない）。

### 12.4 v1 duplicate policy の評価

**提案「変更前に`config_data[4]`をscanし、newRawを別actionが使っていればApply拒否」は、純正のデータ構造上は安全だが、厳しすぎてPoC対象が弾かれる。**
- 純正の重複はcontext単位（§2の規則）。実ファイルでも`COMMAND_MENU=11`・`AUTO_BATTLE=11`・`FAST_FORWARD_TEXT=11`・`SCROLL_SWITCHING=11`がYで共存している。
- PoC対象の「コマンドメニュー Y→X」では、X(12)をPUZZLEの`MENU`が使っている。全体scanだと**拒否になる**。一方、純正UIでは同じ変更がunassignなしで確定している（§19 STEP3のログ。実ファイルで`MENU=12`が残っていることでも裏付け）。

**採用する方針（DESIGN PROPOSAL、根拠はCONFIRMED事実）:**
1. 重複の判定は`ChangeKeyDuplicate`自身に任せる。`dup=true`ならAは変わっておらず（CONFIRMED）、そのままrejectで終わる。rollbackは不要。
2. `dup=true`で書かれる`g_*`は残るが無害。`GamePadDuplicateWrite`を呼ぶのは`EndChoiceUpdate`の種別`0xC8`分岐だけ（CONFIRMED、全呼出元スキャン）。種別`0xC8`のダイアログを開くのは`dds3ConfigGamePadSteam.Updata`だけで（`MsgChoiceMsg`の全9呼出元をスキャン。即値`0xC8`はここだけ）、その直前に自分の`ChangeKey`が`g_*`を上書きしている（CONFIRMED）。`dds3UpdateConfig`内の`MsgChoiceMsg`呼出しのうち2件は引数がレジスタ値で、0xC8でないことを静的に確定できていない（STRONGLY SUPPORTED）。
3. MOD側の事前scanは**表示用のヒント**に留める（Settingsで「同じボタンを使っているaction」を示すだけ）。Applyの可否は決めない。
4. PoCでは`GamePadDuplicateWrite`を呼ばない。

### 12.5 正式Apply transaction（PoCの正本）

```
[Preflight]  1つでも不成立なら何も呼ばずに終了
  P1  FieldDashPatch.IsExplorationActive && authoritative snapshot取得済み
  P2  GameAssembly.dll SHA-256 == 59ADBB5B…A9FC4
  P3  index ∈ {4,5,6,7,12,13,14,15,16,17,18,19,20,23,30}、Type = index+3
  P4  newRaw ∈ {9,10,11,12,13,14,15,16,17,18,25,26}
  P5  sd = GlobalData.saveData != null
      A  = dds3GlobalWork.DDS3_GBWK.config_data[4]（Length==256）
      D  = sd.GetConfigLocal(4)（Length==256）
  P6  A と D が256要素すべて一致（未保存の変更が無い）
  P7  GetConfigGamePad(index) == A[Type] == expectedCurrentRaw、かつ newRaw != A[Type]
  P8  GamePadDoneChk() == true
  P9  SMT3HDCONFIGを読み、[GAMEPAD]のindex 1..33がA[1..33]と一致（ディスクとメモリが一致）

[Snapshot]
  S_A   = A の256要素コピー（復元の正本）
  S_D   = D の256要素コピー
  S_34  = GetConfigGamePad(0..33)
  S_FILE= SMT3HDCONFIGの全文（メモリ上に保持。ログにも出す）

[Native single-rebind]
  bool dup = false;
  dds3ConfigGamePadSteam.ChangeKeyDuplicate((uint)Type, 0, 0, newRaw, ref dup);
  dup == true  → A == S_A を確認して REJECT（dup相手 = g_dstidx - 3 をSettingsへ）。終了
                  A != S_A なら（起きないはず）→ ROLLBACK-1

[Verify config_data]
  A[Type] == newRaw、A[i] == S_A[i]（i≠Type）、GamePadDoneChk() == true
  不成立 → ROLLBACK-1

[Persist]
  sd.SetConfigLocal(4, A)          // D := A → SteamConfigLocalSave → SMT3HDCONFIG 上書き
  例外 → ROLLBACK-2
  D == A（256要素）
  SMT3HDCONFIGを再読込: [GAMEPAD]の該当KEY == newRaw、他の[GAMEPAD]行 == S_FILE、
                        他セクションは S_FILE と一致（GAMEの予約スロット255は出力されないので影響しない）
  不成立 → ROLLBACK-2

[Runtime apply]
  sd.SteamConfigLocalCopy(4, false)   // A, SelText[0][4], SelText[1][4] := D
  例外 → ROLLBACK-2

[GetConfigGamePad readback]
  GetConfigGamePad(index) == newRaw、他の33件 == S_34
  不成立 → ROLLBACK-2

SUCCESS
```

Preflightから最後のreadbackまで同じフレーム内で実行し、途中でyieldしない。

### 12.6 正式Rollback transaction

| ID | 発動条件 | 手順 | 検証 |
|---|---|---|---|
| ROLLBACK-1 | Persist前の失敗（D・ファイルは未変更） | `sd.SteamConfigLocalCopy(4, false)`（A/B/C := D = S_D = S_A） | A == S_A、`GetConfigGamePad(0..33)` == S_34。ディスクには書かない |
| ROLLBACK-2 | Persist以降の失敗（D・ファイルも変わった可能性） | (1) **Native rollback**: A[Type] != S_A[Type]なら`ChangeKeyDuplicate(Type, 0, 0, S_A[Type], ref dup)`。dup==trueまたはA != S_Aなら (1') へ。(1') 代替: S_Aから`Il2CppStructArray<int>(256)`を作り、`sd.SetConfigLocal(4, arr)`で(2)を兼ねる<br>(2) **Persist rollback**: `sd.SetConfigLocal(4, A)` → ファイルを元値で上書き<br>(3) **Runtime apply rollback**: `sd.SteamConfigLocalCopy(4, false)` | D == S_A、A == S_A、`GetConfigGamePad` == S_34、ファイルの[GAMEPAD] == S_FILE |
| ROLLBACK-3 | ROLLBACK-2の途中で例外、または検証不一致 | 以後の編集を無効化する。MODはゲーム側へ追加操作をしない。ログにS_FILEを出し、ユーザーに「ゲーム終了後にSMT3HDCONFIGのバックアップを戻す」よう案内する | — |

- Native rollback（(1)）は「元の値へもう1回rebind」で、純正の重複規則をそのまま通る。元の状態は純正が作った重複のない状態なので、通常は`dup=false`になる（STRONGLY SUPPORTED）。
- 代替(1')は`SetConfigLocal`（純正API）に「S_Aから作った新しい配列」を渡すもの。ゲームのメモリを直接書き換えるわけではない（書くのは純正の要素コピー）。
- どの経路でも最後は`SteamConfigLocalCopy(4,false)`で、A/B/Cを保存済みのDから作り直す。「保存されていない値がliveになる」状態で終わらない。

### 12.7 PoCで呼んではいけないAPI

| API | 理由 |
|---|---|
| `ChangeKey` | live入力依存（CONFIRMED）。押下が無ければ何もしない、押下があればそのボタンで書く |
| `GamePadDuplicateWrite` | 相手をunassignする。`g_*`を検証しないので、古い値で呼ぶとプリセット一括上書きの危険 |
| `cfgSetBit`（直接） | 重複規則を迂回する。Type 0/1でプリセット一括上書き |
| `DateSave` / `DatePrev` / `EndChoiceUpdate` / `dds3UpdateConfig` | Config画面のalias・UI state前提。画面外で`DateSave`を呼ぶと古いCでAを上書きする |
| `ChgConfigGamePad` / `ChgConfigGamePadAll` | 前者は未解明、後者は何もしない |
| `SteamOptionFile.LoadFile` / `FsSaveData.SteamConfigLocalLoad` | 読込み失敗時の分岐で`ConfigAllDateInit2`（既定値初期化）を呼ぶ |
| `SteamConfigLocalCopy(-1, …)` | 全タブを上書きする。PoCはtab4だけ |
| `SetConfigLocal(tab≠4, …)` | PoC範囲外 |
| `SelText`・`g_*`・`ChangeTexEXE`への直接write | 直接memory write（禁止） |

### 12.8 PoC開始可否

**判定: 開始可（条件付き）。** single rebindのentry point・保存・runtime反映・rollbackは、すべてbyte-exactで確定した純正public API（proxy上）だけで組める。直接memory writeは不要。

開始条件:
1. PoC前に、**ゲームを終了した状態で**ユーザーが`SMT3HDCONFIG`を手動バックアップする（ROLLBACK-3の最終手段）。
2. PoCの対象は **index 7「コマンドメニュー」Y(11)→X(12)→Y(11)の1件だけ**。純正UIで同じ変更が重複なしで確定した実績がある（§19 STEP3）。
3. 実行は一時的なデバッグトリガー（1回押して1回実行）にし、§12.5の各段階の値をすべてログに出す。
4. 確認項目: 同一セッションのreadback、SMT3HDCONFIGの`COMMAND_MENU=`行の変化、ゲーム内でXでコマンドメニューが開くこと、純正Config画面の表示と「無変更で閉じて保存確認が出ない」こと、再起動後のreadback、Yへ戻すA/B/A。
5. PoCで確認するUNRESOLVED: ゲームの実入力がすぐ追従するか（§11-4）、ボタンガイドのアイコンが更新されるか（`ChangeTexEXE`、§11-5）。アイコンが古いままでも、v1をブロックする問題ではない（再起動で直るかを記録する）。

---

## 変更ファイル / git状態（2026-09-24）

本セッションでは本ファイル（`investigations/NATIVE_GAMEPAD_EDIT_DESIGN_20260924.md`、新規）の作成と、§12の追記（同日続き、既存節の表現訂正を含む）のみを行った。`SMT3HDCONFIG`は読み取り専用で表示しただけで、変更・コピー・移動はしていない。既存ファイル（`SESSION_RESUME_NOTES.md`、`src/ModMain.cs`、`src/GameBindingDiagnosticProbe.cs`、`investigations/NATIVE_GAMEPAD_CONFIG_DATAFLOW_20260922.md`を含む）は変更していない。解析用Pythonスクリプトはscratchpad（リポジトリ外）にのみ存在する。native呼出・ゲーム設定/セーブデータへのwrite・build・deploy・commit・push・branch切替・stash操作は一切行っていない。

---

## 13. PoC実機結果と「終了確認のカーソル消失」切り分け（2026-09-26）

### 13.1 トリガー不発の原因（解決）

- 初回（12:55）の「何も起きなかった」は、ログ上`ARMED`止まり（`[NMC-GAMEWRITE]`は2件のみ、write 0回）。
- 原因: ユーザーのキーボードは**Fnを押さないとF9/F10がOSへ送られない**。native write系の問題ではない。
- `TRIGGER_STATE`診断ログ（キー/ゲート状態のエッジ、HOLD_START/500ms/1000ms/THRESHOLD/RELEASEのみ）を追加し、以後のトリガーは実機で正常動作を確認。
- 補足: ゲームは約145fpsで動作しており、`HoldFrames=90`は実際には約0.6秒（コメントの「~1.5s at 60fps」とは異なる）。未修正。

### 13.2 write transaction の実機結果

| 項目 | Evidence |
|---|---|
| STEP B（X→Y）13:03:53: PRECHECK→REBIND（dupflg=False、`config_data[4][10]` 12→11のみ）→PERSIST→FILE_HASH（`COMMAND_MENU`行のみ変化）→RUNTIME_APPLY→READBACK（34スロット中index7のみ12→11） → `STEP_B_SUCCESS`、rollbackなし | CONFIRMED（ログ） |
| STEP B後のSMT3HDCONFIG = PoC前バックアップとbyte一致（`8C308677…`） | CONFIRMED |
| STEP A（Y→X）13:25:23: 同じ全段階OK → `STEP_A_SUCCESS` | CONFIRMED（ログ） |
| STEP Aの結果が再起動後も保持される（13:03起動時`STARTUP index=7 raw=12`、タイトル初期化での12→11上書きなし、ファイル/`config_data[4]`/`FsSaveData`/`GetConfigGamePad(7)`一致） | CONFIRMED（ただしその回のSTEP Aログ自体は上書きで消失、成功はユーザー申告） |
| 13:32起動時点でSMT3HDCONFIGがX状態（`9AD11F62…`、mtime 13:31:35）である理由 = 直前セッションでのPoC STEP A成功＋永続保存 | STRONGLY SUPPORTED（該当セッションのログは上書き済み、断定しない） |

### 13.3 「ゲーム終了確認のカーソル/ハイライト消失」切り分け

症状（ユーザー観察）: キャンプメニュー「ゲーム終了」の はい/いいえ で**カーソルとハイライトが消える。上下移動・決定/キャンセルの入力は効く**。戦闘に入って戻ると復旧。

診断: `src/UiRefreshDiagnosticProbe.cs`（`[NMC-UI-DIAG]`、read-only）。観測点: BASELINE_FIELD / AFTER_STEP_x_SUCCESS(+30f) / `cmpUpdate.cmpUpdateGameEnd`のENTER・SHOWN / 戦闘後のFIELD復帰(+30f)。各点で`IsChangeTexEXE/NG`、`GetConfigGamePad(7)`、`GetConfigGamePadGuideKID(0..40)`の非恒等値、`ButtonGuide`状態、有効な`ChangeTex`のsprite名を記録。

決定的セッション（13:32起動）:

| 時刻 | 操作 | write | index7 | 結果 |
|---|---|---|---|---|
| 13:33:14〜24 | 戦闘→フィールド復帰 | 0 | 12(X) | — |
| 13:33:31 | 終了確認#1（キーボード操作前） | 0 | 12(X) | 正常（ユーザー確認待ち） |
| 13:34:09 | Fn+Ctrl+Shift+F9 を126ms/18frameのみ（STEP A不成立、`STEP_A BEGIN`なし） | 0 | 12(X) | — |
| 13:34:16 | 終了確認#2 | 0 | 12(X) | **カーソル/ハイライト消失を再現** |

**CONFIRMED**:
- この再現セッションではGAME binding writeは0回。
- index 7は起動時からraw12(X)。
- Fn+Ctrl+Shift+F9は126ms/18frameのみでSTEP A不成立。
- それでもキーボード入力後の終了確認でカーソル/ハイライト消失を再現。
- `ChangeTexEXE`/`ChangeTexNG`、`GetConfigGamePad(7)`、GuideKID、ButtonGuide状態は終了確認#1/#2で差が無かった。
- よって今回のカーソル消失はGAME binding write経路とは独立。
- 別セッション（13:25）でも、純正Configで一度Yに戻して保存した後もカーソル消失は継続し、そのとき有効な`ChangeTex`55件のspriteは症状発生時と完全一致した。

**STRONGLY SUPPORTED**:
- キーボード入力によってゲーム側のinput-device modeがkeyboard/mouse側へ切り替わり、選択UIのcontroller cursor/highlight表示が抑制されている。
- 戦闘遷移でinput/UI状態が再初期化され、controller側へ戻るため復旧する。

**FALSIFIED**:
- `ChangeTexEXE`を立てないことが今回のカーソル消失原因。
- index 7をXへ変更したこと自体が今回のカーソル消失原因。
- PoCの`SetConfigLocal` / `SteamConfigLocalCopy`が今回の直接原因。

**UNRESOLVED**:
- keyboard/mouse modeを保持している具体的なnative/managed state。
- 戦闘で復旧する正確なrefresh経路。

### 13.4 静的解析の副産物（参考、CONFIRMED by raw disasm）

- `ChangeTexEXE`（`dds3ConfigMainSteam` static `+0x1D9`）の読み手は`ChangeTex.Update`（`0x182038982`）のみ。true時に`bRedefine=1`→`redefinition_local`でsprite再生成。
- 書き手: `DateSave`（`0x1822BAFD0`、=1）、`DefaultPrev`（`0x1822BB3E1`、=1）、`dds3UpdateConfig`フレーム先頭（`0x1822C9E2F`、=0）、`.cctor`。→ 純正では「Config画面内で1フレームだけ立つpulse」。
- `ChangeTexNG`（`+0x1D8`）がtrueだと`ChangeTex.Update`は何もしない。Set元は`dds3CalcConfig`/`dds3DrawConfig`、Clr元は`dds3UpdateConfig`/`dds3ConfigProcessStart`。
- `buttonguideUI`の再定義は`ButtonGuide.ButtonGuideDispSet`→`buttonguideUI.Redefinition`経由（`ChangeTexEXE`非依存）。
- キャンプ「ゲーム終了」= `cmpUpdate.cmpUpdateGameEnd` → `fclMisc.fclStartSelMessage`/`fclChkSelMessage`（はい/いいえ）→ `SteamExitDialog.RequestExit`。

### 13.5 方針

- **PoC write transaction自体は変更しない。** `ChangeTexEXE`を立てる修正は不要（13.3でFALSIFIED）。
- カーソル消失はPoC用キーボードホットキーで露呈したゲーム本体側の挙動と判断し、keyboard modeの解析はいったん止める。正式実装はSettings GUI（ゲーム外プロセス）から操作するため、原則として無関係。
- 診断コード `GameBindingDiagnosticProbe` / `GameBindingWritePoc`内のtrigger diagnostics（`TRIGGER_STATE`） / `UiRefreshDiagnosticProbe` は正式実装前のcleanup候補。現時点では削除しない。
- 次の優先作業: 現在X状態を利用してSTEP B（Fn+Ctrl+Shift+F10長押し、X→Y）を実行し、(1)`STEP_B_SUCCESS` (2)実入力がYへ戻る (3)純正Config表示がY (4)SMT3HDCONFIGがY (5)完全再起動後もY、を確認する。成功すればsingle-rebind transactionの往復PoCを実質成功と判定する。

---

## 14. 正式実装 Step 1 — Request Protocol + Transaction Core（2026-09-26）

### 14.1 構成

```
Settings.exe --(atomic write)--> NocturneModernController.game-binding-request.json
   (Settings終了)                              |
Core: SettingsGuiController(プロセス終了検出) → GameBindingRequestProcessor.ProcessPending()
   → request読込 → 即delete → GameBindingWriteService.HandleJson()
   → NocturneModernController.game-binding-result.json (atomic write)
   → 状態が変わり得た場合はauthoritative snapshot(actions.json)を再publish
```

- 既存`feature-requests.json`と同じ慣例（Mods直下、Settingsがatomic write、CoreはSettingsプロセス終了時に1回処理、処理後delete、JSONは`System.Text.Json`既定=PascalCase）。新規IPCなし、毎frame parseなし。
- 純粋ロジックは`shared/GameBindingWrite.cs`（Settings/testsと共有可能）。native呼出しは`INativeGameBindingPort`越し、Il2Cpp実装は`src/NativeGameBindingPort.cs`。
- **Step 1ではwrite無効**: `GameBindingRequestProcessor.WritesEnabled = false`。dry-runのみ実行され、非dry-runは全precheck後に`WRITES_DISABLED`でrejected（native write 0回）。

### 14.2 Request（schema 1）

| field | 型 | 必須 | 内容 |
|---|---|---|---|
| `SchemaVersion` | int | ○ | 1 |
| `RequestId` | string | ○ | 1〜64文字 `[A-Za-z0-9_-]`。セッション中one-shot |
| `Operation` | string | ○ | `rebind`のみ |
| `ActionIndex` | int | ○ | `GetConfigGamePad` index。CONFIRMED 15件のみ |
| `ExpectedCurrentRaw` | int | ○ | Settingsが表示していた値。現在値と不一致ならSTALE |
| `NewRaw` | int | ○ | A=10 B=9 X=12 Y=11 LB=13 LT=14 RB=15 RT=16 L3=17 R3=18 SELECT=25 START=26 |
| `DryRun` | bool | - | trueなら全precheckのみ（write 0） |

### 14.3 Result

`SchemaVersion, RequestId, Status, ErrorCode, ActionIndex, BeforeRaw, AfterRaw, ConflictActionIndex, DryRun, Message`

- Status: `success` / `validated`（dry-run） / `rejected`（write 0） / `rollback_success` / `rollback_failed`（`failed`は予約）
- ErrorCode: `MALFORMED_REQUEST` `UNSUPPORTED_SCHEMA` `UNSUPPORTED_OPERATION` `DUPLICATE_REQUEST_ID` `UNSUPPORTED_ACTION` `UNSUPPORTED_BUTTON` `NO_CHANGE` `NOT_READY` `UNSUPPORTED_GAME_BUILD` `STALE_CURRENT_VALUE` `INCONSISTENT_STATE` `WRITES_DISABLED` `SESSION_LOCKED` `DUPLICATE` `NATIVE_WRITE_FAILED` `PERSIST_FAILED` `RUNTIME_APPLY_FAILED` `READBACK_FAILED` `INTERNAL_ERROR`
- rollback時のErrorCodeは「失敗した段階」。rollbackの成否はStatusで表す。

### 14.4 Transaction（PoC §12.5の順序を維持）

1. shape検証（schema/requestId/operation/CONFIRMED action/対応button/NO_CHANGE）→ requestId登録
2. session lock・readiness（`IsExplorationActive && GameActionBindingsReady`）・GameAssembly.dll SHA-256（セッション1回だけ計算）
3. `GetConfigGamePad(index) == ExpectedCurrentRaw`
4. snapshot（`config_data[4]`全256、FsSaveData全256、`GetConfigGamePad` 34値）。`config_data == FsSaveData`、`config_data[index+3] == 現在値`、`GamePadDoneChk()`
5. DryRunなら`validated`で終了 / write無効なら`WRITES_DISABLED`
6. `ChangeKeyDuplicate(index+3, 0, 0, NewRaw, ref dup)` → dup=trueかつ無変更なら`rejected/DUPLICATE`（`ConflictActionIndex = g_dstidx - 3`）。`GamePadDuplicateWrite`は呼ばない。MOD側の全体scanによる重複判定はしない
7. diffが対象要素のみ＋`GamePadDoneChk()`
8. `FsSaveData.SetConfigLocal(4, config_data[4])` → FsSaveData == config_data
9. `FsSaveData.SteamConfigLocalCopy(4, false)`
10. readback: `GetConfigGamePad` 34値のdiffが対象indexのみ、config_data diffが対象要素のみ、config_data == FsSaveData

rollback: 8より前の失敗は`SteamConfigLocalCopy(4,false)`のみ。8以降は逆`ChangeKeyDuplicate`（失敗時はsnapshot配列で`SetConfigLocal`）→`SetConfigLocal`→`SteamConfigLocalCopy`。復元検証（3層＋34値）に失敗したら`rollback_failed`、以後そのセッションは`SESSION_LOCKED`。

### 14.5 PoCから変えた点（再評価の結果）

- **SMT3HDCONFIGの全bytes snapshot／内容比較は製品版では行わない**。PoCで`SetConfigLocal`の出力が「`COMMAND_MENU`行だけ変わる」ことを2回byte-exactで確認済み。ファイルはFsSaveDataから書き出されるため、FsSaveData（全256）のsnapshotとrollback（`SetConfigLocal`で書き戻す）で正確性は保たれる。ファイルのパス探索やAPPDATA読込を製品動作に持ち込まない。
- PoC固定の外部backupパス／期待hashは持ち込まない。
- keyboard trigger・hold timer・`TRIGGER_STATE`・大量ログは持ち込まない。ログは`[NMC-GAMEBIND] request=… status=… action=… before=… after=… dryRun=…`の1行（失敗時はWarningで`error=`付き）。
- GameAssembly.dll hash確認は維持（native解析対象ビルド以外ではwriteしない）。

### 14.6 既知の課題（Step 2で扱う）

- Settingsの表示（`GameActionBindingDisplayFormatter`）は、index毎にA/B/A確認済みのraw→ボタン対応だけを表示する。書込み可能な12ボタンのうち、そのindexで未確認のrawへ変更すると、その行が表示から消える。Step 2では書込み対象ボタン表（`GameBindingWriteProtocol.SupportedButtons`）と表示側の対応を揃える必要がある。
- Settingsは結果ファイルを次回起動時に読む（Core処理はSettings終了時）。UI上の見せ方はStep 2で決める。

---

## 15. 正式実装 Step 2 — Settings編集UI（2026-09-26）

### 15.1 Evidenceと製品仕様の分離

| 概念 | 場所 | 意味 |
|---|---|---|
| `GameActionBindingRawEntry.ConfirmedPhysicalButton` | `shared/GameBindingSnapshot.cs`（`ConfirmedPhysicalButtonsByIndex`、変更なし） | そのindexでA/B/Aにより直接確認したraw→ボタン。**Evidence**。actions.jsonにもそのまま残る |
| `GameBindingSupportedButtons` | `shared/GameBindingSnapshot.cs`（新規） | v1が**表示・書込み対象として扱う12 raw**（A=10 B=9 X=12 Y=11 LB=13 LT=14 RB=15 RT=16 L3=17 R3=18 SELECT=25 START=26）。A/B/A試験で観測されたコードを全confirmed actionへ適用する**製品仕様**であり、native enumを解明したという主張ではない |

- write検証（`GameBindingWriteProtocol.ValidateShape`）と表示の両方が`GameBindingSupportedButtons`を参照（Step 1の`SupportedButtons`辞書はこちらへ統合）。
- 表示の行条件を`ConfirmedPhysicalButton != null`から`ConfirmedActionName != null`へ変更。15 actionは現在rawに関係なく常に表示し、12以外は`Unknown (raw=N)`（行は消さない）。

### 15.2 編集モデル（`shared/GameBindingEdit.cs`、pure）

- `GameBindingEditModel(entries, authoritative)`: 編集可能 = authoritative かつ confirmed action かつ現在rawがsupported。
- `SetSelection(index, raw)`: supported rawのみ受理。現在値と同じなら変更扱いにしない。
- `ApplyState`: `NotAuthoritative` / `NoChanges` / `MultipleChanges`（v1は1回1 action） / `UnknownCurrent` / `Ready`。
- `BuildRequest(idFactory)`: Readyのときだけ、Step 1 schemaのrequest（`ExpectedCurrentRaw` = Settings起動時のsnapshot値、`NewRaw` = 選択値、`DryRun=false`）を返す。
- `GameBindingResultMessages.Describe(result, japanese)`: errorCode→UI文言（codeと文言を分離）。DUPLICATEは要求ボタンと衝突相手の項目名を表示するため、Resultに`RequestedRaw`を追加した。

### 15.3 Settings UI（`settings/Program.cs`）

- タブ名: `GAMEキーコンフィグ` / `GAME Bindings`（「参照専用」を削除）。
- 列: 項目 / 現在 / 変更後（12ボタンのドロップダウン）。Unknown現在値の行は表示のみ（ドロップダウン無効）。
- ボタン: 「適用」= requestファイルをatomic writeして予約（以後その画面の編集をロック）、「取り消し」= 予約済みrequestを削除し、未適用の選択を元に戻す。
- **確定は既存タブと同じく「OK / 保存」**。OK / 保存で閉じたときだけ予約requestが残り、Core（Settings終了時の`GameBindingRequestProcessor`）が処理する。フッターのキャンセル・×で閉じた場合は予約requestを削除する（`FormClosing`）。Settings終了そのものが自動Applyになることはない（明示的な「適用」操作が必要）。
- Settings起動時: 前回セッションの残りrequestは削除（OK / 保存で確定されていないため）。resultファイルがあればタブ上部に「前回の変更: …」として1回表示し、削除。
- authoritativeでない場合: 行を出さず「フィールドへ移動してからSettingsを開き直してください」を表示（既存readiness方針のまま）。
- 成功後の表示はSettings側で`NewRaw`を正とせず、Coreがtransaction後に再publishしたactions.jsonのauthoritative snapshotを次回起動時に読む。
- request/resultパスはCoreから引数6/7で渡す（無い場合はsettings.jsonと同じフォルダの既定名）。

### 15.4 Step 3に向けた注意

Coreは`WritesEnabled = false`のまま。UIから「適用」→「OK / 保存」を行うと、Coreは全precheck（readiness、GameAssembly hash、STALE、config_data/FsSaveData整合、GamePadDoneChk）を実行した上で`rejected/WRITES_DISABLED`を返し、native writeは0回。次回Settingsに「このバージョンでは…変更は無効です」と表示される。これを実質的なend-to-end dry-runとして使える。

---

## 16. 正式実装 Step 3 — End-to-End Dry Run（2026-09-26）

### 16.1 実機で見つかった不具合と修正（CONFIRMED）

- 1回目の実機試験（15:03起動、Core `7ADC6614…`）では、Settings終了時の要求がすべて`rejected/NOT_READY`（15:06:17・15:08:42）。
- 原因: `FieldDashPatch.IsExplorationActive`は「直近100ms以内にフィールド更新が走ったか」。Settings表示中はゲームウィンドウが最小化されフィールド更新が止まるため、Coreが「Settingsプロセス終了」を検知した同じフレームでは必ずfalse。
- 修正: `SettingsGuiController`はSettings終了時に`GameBindingRequestProcessor.OnSettingsClosed()`で「要求あり」を記録するだけにし、`GameBindingRequestProcessor.Sample()`（`ModMain.OnUpdate`）がexploration再開を待って処理する。5秒以内に再開しなければその場で処理（=`NOT_READY`）し、無関係な後のタイミングで適用されることはない。
- NOT_READYはnative読取り前に止まるため、1回目の試験でもnative write 0（SMT3HDCONFIG不変）。

### 16.2 E2E dry-run結果（16:49起動、Core `B6DCE73D…`、Settings `A36B280A…`）— VERIFIED

| 項目 | 結果 |
|---|---|
| authoritative snapshot | 16:49:53 `ready … (34/34)` |
| 生成request（1回目試験で★時点にファイル直接確認） | `SchemaVersion=1, RequestId=<GUID 32hex>, Operation=rebind, ActionIndex=7, ExpectedCurrentRaw=11, NewRaw=12, DryRun=false`。`.tmp`残骸なし |
| Core処理 | Settings終了16:50:17.522 → 362ms後 16:50:17.884 `[NMC-GAMEBIND] request=e8f45421… status=rejected action=7 before=11 after=11 error=WRITES_DISABLED` |
| 通過したprecheck | shape（schema/requestId/operation/action/button）→ readiness → GameAssembly hash → `GetConfigGamePad(7)=11==Expected` → config_data==FsSaveData → `config_data[10]==11` → GamePadDoneChk → WRITES_DISABLEDで停止 |
| native write | 0（`WRITES_DISABLED`は`Write()`より前。`ChangeKeyDuplicate`/`SetConfigLocal`/`SteamConfigLocalCopy`未到達） |
| SMT3HDCONFIG | 不変（`8C308677…`、mtime 13:46:22のまま） |
| actions.json | Authoritative=True、index 7 = raw 11 (Y) |
| result | 16:50:33のSettingsで表示・消費（resultファイル削除済み）、16:51:07の再オープンでは再表示なし（ユーザー確認OK） |
| UI | 15項目、12ボタン、タブ名、複数変更で適用無効、取り消しで要求なし（ユーザー確認OK） |
| 旧診断ログ | 0件 |
| 例外 | 既存のもののみ（SMART-AUTO inspect、PreyEyes2 NRE、MelonStartScreen、FileSystemWatcher） |

判定: **Step 3 E2E dry-run VERIFIED**。Unknown raw UIは実機で作らず、unit test（`GameBindingEditTests`）を正とする。

---

## 17. 正式実装 Step 4 — First Real Write via Settings（2026-09-26）— REAL WRITE VERIFIED

### 17.1 変更

- `src/GameBindingRequestProcessor.cs`: `WritesEnabled = false` → `true`（コメント更新のみ）。transaction・検証・schema・UI・待機・rollback・duplicate処理は無変更。
- Core `DE3D856154500841D65567D93CBF8B4F9EFBC08051378EDE05B17D8FA656D644`（build=deploy一致）。Settings `A36B280A…`はbyte-identicalのため再deployなし。
- 開始前backup: `C:\SMT3Modding\backups\SMT3HDCONFIG\SMT3HDCONFIG.pre-step4-realwrite-20260926_165630`（2975 bytes、mtime 13:46:22、SHA-256 `8C30867747AD…EDBC5C`、`BACKUP_LOG.txt`へ記録）。

### 17.2 実機結果（index 7「コマンドメニュー」のみ）

| 段階 | 証拠 | 結果 |
|---|---|---|
| STEP A（17:04起動） | `[NMC-GAMEBIND] request=3d4369ad… status=success action=7 before=11 after=12`（Settings終了から367ms後） | 成功、rollbackなし |
| STEP A ファイル | SMT3HDCONFIG 17:05:36、`9AD11F6220A7…F075AABF`。backupとのdiffは`COMMAND_MENU=11→12`の1行のみ | 成功 |
| STEP A snapshot | actions.json Authoritative=True、index 7 = 12 | 成功 |
| STEP A 実入力・純正Config・Settings表示 | ユーザー確認OK | 成功 |
| STEP A 再起動保持（17:08起動） | STEP Bのprecheckで`GetConfigGamePad(7)=12 == ExpectedCurrentRaw`（`before=12`）、Settings現在値X（ユーザー確認） | 保持 |
| STEP B | `request=1b820afb… status=success action=7 before=12 after=11` | 成功、rollbackなし |
| STEP B ファイル | SMT3HDCONFIG 17:09:04、`8C30867747AD…EDBC5C` = 開始前backupと**byte一致**（`cmp`） | 完全復元 |
| STEP B 実入力・純正Config | ユーザー確認OK | 成功 |
| STEP B 再起動保持（17:11起動） | 17:11:53 authoritative snapshot、actions.json index 7 = 11 | 保持 |
| エラー | 既存（MelonStartScreen、FileSystemWatcher）のみ。Harmony/例外の新規なし | — |

判定: **Step 4 REAL WRITE VERIFIED**（正式Settings UIからのY→X→Y往復、実入力・純正Config反映、SMT3HDCONFIG永続化、再起動後保持、開始前状態へのbyte一致復元、rollback不要）。

未観測1件: success結果の「前回の変更: …に変更しました」表示。STEP B後のresultファイル（`Status=success, BeforeRaw=12, AfterRaw=11`）は未消費のまま残っており、次回Settings起動時に表示・削除される。結果表示の仕組み自体はStep 3（WRITES_DISABLED）で実機確認済み。

---

## 18. Step 5 — Formalization / Commit Preparation（2026-09-26、未deploy・未commit）

### 18.1 変更

- `WritesEnabled`: Core側の一時const（`GameBindingRequestProcessor.WritesEnabled`）を削除し、`new GameBindingWriteService(port, writesEnabled: true)`で常時有効。サービス側の書込み無効モード（`WRITES_DISABLED`）は、テストと将来の緊急停止用の機能として残す（呼出し側は1か所）。
- UI文言: rollback系メッセージと既定メッセージから内部errorCodeの表示を削除。`NO_CHANGE`と「処理できなかった」系（MALFORMED/UNSUPPORTED_SCHEMA/OPERATION/DUPLICATE_REQUEST_ID/INTERNAL_ERROR）に専用文言。
- result消費: Settingsの「読んで削除」を`shared/GameBindingEdit.cs`の`GameBindingResultFile.Consume`へ切り出し（壊れたresultも破棄）。
- コメントから「Step N」表記を除去。
- tests追加: 全status×全errorCode×日英でUI文言にerrorCodeが出ないこと、resultの1回消費・再表示なし・破損破棄、Settings request JSONがCoreで同一にparseされること。

### 18.2 Safety review（静的、ビルド対象ソースのみ）

| 項目 | 結果 |
|---|---|
| confirmed 15 actionのみ | `IsSupportedAction` → `ConfirmedActionNames` |
| supported 12 buttonのみ | `GameBindingSupportedButtons`（UI選択肢・Core検証で共通） |
| 1 Apply = 1 action | `GameBindingEditModel.ApplyState`、requestは単一action schema |
| duplicate reject | `ChangeKeyDuplicate`のdupflgのみを正とし`rejected/DUPLICATE` |
| expectedCurrentRaw必須・stale拒否 | `ValidateShape`必須、`GetConfigGamePad==Expected`以外は`STALE_CURRENT_VALUE` |
| authoritative・exploration後処理 | port `IsReady`＋processorがexploration再開を最大5秒待つ |
| rollback | 保存前/後の2種、失敗時session lock |
| native write順序 | `ChangeKeyDuplicate`→`SetConfigLocal(4)`→`SteamConfigLocalCopy(4,false)`→readback。native write呼出しは`NativeGameBindingPort`の3か所のみ |
| 禁止API | `GamePadDuplicateWrite`/`ChangeKey`/`cfgSetBit`/`DateSave`/`ChgConfigGamePad*`/`ChangeTexEXE`/`SteamConfigLocalCopy(-1)`: ビルド対象に呼出しなし（コメントのみ）。`Marshal.Write*`は既存`FieldDashPatch`のダッシュ速度パッチのみで本機能と無関係 |
| Settingsから直接native write | なし（SettingsはIl2Cpp参照0、requestファイルを書くだけ） |

### 18.3 調査・PoCファイルの扱い（方針）

既存の慣例（`src`直下に置いたまま`Compile Remove`、例: `ForceEncounterTelemetry.cs`、`NativeMouseVerticalCameraPoc.cs`）に合わせ、移動しない。

| ファイル | 状態 | 方針 |
|---|---|---|
| `src/GameBindingWritePoc.cs` | untracked、Compile Remove | reference（実証済みtransactionの原本）としてcommit候補 |
| `src/GameBindingDiagnosticProbe.cs` / `src/UiRefreshDiagnosticProbe.cs` | untracked、Compile Remove | 同上（調査の再現用） |
| `src/PadCfgDiagnosticProbe.cs` / `src/GameBindingProbe.cs` | tracked、Compile Remove | そのまま |
| `investigations/*.md`（2件） | untracked | 調査記録としてcommit候補 |

公開パッケージ（DLL）にはどれも含まれない。

### 18.4 Final smoke test（2026-09-26 17:22〜17:23、Core `0EF7E101…`、Settings `7406E5AA…`）— FINAL SMOKE VERIFIED

- 17:22:25 authoritative snapshot（34/34）。actions.json Authoritative=True、index 7 = 11 (Y)。
- 17:22:28 Settings: 未消費だったSTEP B成功result（`BeforeRaw=12, AfterRaw=11`）の「前回の変更: コマンドメニュー を X → Y に変更しました。」表示、現在値Y、15項目・12ボタン・適用/取り消し、errorCode露出なし（ユーザー確認OK）。resultファイルは消費・削除済み。
- 17:22:40 / 17:23:09 再オープン: 成功メッセージ再表示なし（ユーザー確認OK）。
- `[NMC-GAMEBIND]`出力なし（requestなし＝書込みなし）。SMT3HDCONFIG不変（`8C308677…`、mtime 17:09:04）。
- 旧診断ログ0件。例外は既存（MelonStartScreen、FileSystemWatcher）のみ。
- これで Step 4 の未観測事項（success結果の表示）も実機確認済み。
