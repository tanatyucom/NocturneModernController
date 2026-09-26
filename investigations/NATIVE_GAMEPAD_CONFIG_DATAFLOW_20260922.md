# 純正Controller Key Config データフロー解析（READ-ONLY / ANALYSIS ONLY）2026-09-22

目的: MOD Settingsから純正GAME bindingを書き換える将来実装に備えて、プリセットA/B/C → 個別変更 → 保存 → runtime反映のデータフローを解析する。**今回はコード変更・build・deploy・native呼出は一切行っていない。**

対象バイナリ: `GameAssembly.dll` SHA-256 `59ADBB5B18AAEDC7DF6C1672790CA48BE99B5E8559C81DB27132C436FB0A9FC4`（`SMT3HD_GameAssembly_METADATA.md`と同一）。

## 0. 今回使った資料（既存資産の再利用のみ、新規decompileなし）

- `investigations/ghidra/ChangeKey_18228E320_decompile.txt`（前回セッション作成、Ghidra decompile）
- `investigations/ghidra/ChangeKeyDuplicate_18228DEA0_decompile.txt`（同上）
- `investigations/ghidra/FUN_182296EF0_decompile.txt`（同上）
- `investigations/ghidra/FUN_1822EE830_decompile.txt`（同上）
- `C:\SMT3Modding\NocturneModernGameplay\.analysis\cpp2il_cs\DiffableCs\Assembly-CSharp\dds3ConfigGamePadSteam.cs`（Cpp2IL、フィールドoffset付き）
- `C:\SMT3Modding\NocturneModernGameplay\.analysis\cpp2il\IsilDump\Assembly-CSharp\dds3ConfigGamePadSteam.txt`（Cpp2IL ISIL、命令列。GetConfigGamePad/ChgConfigGamePadAll/GetGamePadPriSet/OnPresetの実体を新たに参照）
- `C:\SMT3Modding\NocturneModernGameplay\.analysis\cpp2il_cs\DiffableCs\Assembly-CSharp\dds3GlobalWork_H\dds3GlobalWork_t.cs`（`config_data`フィールドoffset確認用）
- Ghidra Project本体（`SMT3HD_GameAssembly`）は**今回開いていない**（GUI/headless操作なし。既存exportテキストのみ参照）。`NocturneModernGameplay/.analysis`はSMT3HD全体の共通解析資産として既に存在していたものを読んだだけで、変更していない。

**重要な限界**: Cpp2IL ISILはベクトル化/`rep movs`系の一括コピー命令を正しくリフトできないことがあり、後述のとおり`ChgConfigGamePadAll`の実データコピー命令が今回のダンプ上で見えていない。この点はGhidra decompileでの再確認が必要（§9参照）。

---

## 1. Known facts（CONFIRMED — decompile/フィールドoffsetから直接確認）

### 1.1 `dds3ConfigGamePadSteam`の静的フィールド（`DAT_182e4e3b8`がこのクラスの static-fields anchor）

| offset | 名前 | 型 |
|---|---|---|
| 0x0 | cfgGamePadRecv | SteamCollidersReceiver |
| 0x8 | noselIndex | int |
| 0xC | arrowIndex | int |
| 0x10 | minMaxIndex | int |
| 0x18 | mEnter | Boolean[] |
| 0x20 | mListName | String[] |
| 0x28 | key_change_mode | int |
| 0x2C | key_mode | int |
| 0x30 | UpdateControllerIcon | bool |
| 0x38 | iumap | UInt64[][] |
| 0x40 | **g_chgkey** | int |
| 0x44 | **g_srcidx** | int |
| 0x48 | **g_dstidx** | int |
| 0x50 | padbutton_sprite_name | Dictionary<Int16,String> |
| 0x58 | **config_gamepad_priSet** | **Int32[][]** |
| 0x60 | **chg_padmap** | **Int32[][]** |

`g_chgkey`/`g_srcidx`/`g_dstidx`（0x40/0x44/0x48）は`ChangeKeyDuplicate`(`FUN_18228dea0`)が重複検出時に書き込む先とdecompile上で完全一致（後述1.3）。

### 1.2 `dds3GlobalWork_t`（instance fields、`DAT_182e4ed30`が`dds3GlobalWork`クラスのstatic anchor、`+0xb8`を二重derefして`.Instance`を得るパターン＝singleton）

| offset | 名前 | 型 |
|---|---|---|
| 0xE8 | **config_data** | **Int32[][]** |

`ChgConfigGamePadSteam`側の`DAT_182e4ed30`アクセスパターン（`**(DAT_182e4ed30+0xb8)+0xe8`）は、この`config_data`フィールドのoffsetと完全一致。

### 1.3 write path: `ChangeKey` → `ChangeKeyDuplicate` / `FUN_182296ef0`

- `ChangeKey(UInt32 Type, ref Boolean dupflg)`（`FUN_18228e320`）は`Type`(=`param_1`、action id、少なくとも3〜0x21を扱う)によるswitchで6グループに分岐。`case 0x17`のみ`FUN_182296ef0`を直接呼び、他は`ChangeKeyDuplicate`(`FUN_18228dea0`)を呼ぶ。
- `ChangeKeyDuplicate(Type, st_idx, ed_idx, chgkey, ref dupflg)`は`st_idx..ed_idx`の範囲で「既に`chgkey`と同じ値が割り当てられているaction」を`dds3ConfigGamePadSteam.chg_padmap[Type][0]`を手がかりに全action(0..33)を線形走査して探す。
  - 重複が見つかった場合: `g_chgkey=chgkey, g_srcidx=Type, g_dstidx=(見つかったaction index)`を書き込み、`dupflg=true`で戻る（**実際の書込みはまだ行わない** — 呼び出し元UIに「重複あり、swapしますか」を委ねるための一時停止と解釈できる、STRONGLY SUPPORTED）。
  - 重複が見つからなかった場合: `FUN_182296ef0(Type, dds3GlobalWork.Instance.config_data + 0x40, chgkey, 1, 0)`を呼ぶ。
- `FUN_182296ef0(uint idx, longlong* arrayFieldAddr, int value, char applyToAll)`が実際の書込み本体:
  - `idx`が0,1: `arrayFieldAddr`が指す配列の要素`[idx]`へ`clamp(value,0,4)`を書き込み、`applyToAll!=0`のとき**同じ値を0..0x21全要素へブロードキャストコピー**する分岐あり。
  - `idx`が2: 別要素(+0x28)へ`clamp(value,0,4)`を書き込み。
  - `idx`が3..0x21: **`arrayFieldAddr配列[idx] = value`をそのまま書き込むだけ**（単純な1要素書換え）。
  - `ChangeKey`/`ChangeKeyDuplicate`どちらの経路でも、渡される`arrayFieldAddr`は常に`dds3GlobalWork.Instance.config_data + 0x40`（= `config_data`という`Int32[][]`の**要素[4]へのアドレス**、`0x40 = 0x20(array data start) + 4*8`）。

**CONFIRMED（高確度）: 純正GAME keyconfigの「1アクションだけ変更する」書込み先は `dds3GlobalWork.Instance.config_data[4][actionId]`（`actionId`は3〜0x21、0..2は特殊扱い）。**

- 全パス共通で末尾に`FUN_1822ee830(1)` → `FUN_1822ee540(1,0,0)`を呼ぶ（役割は前回セッションからUNRESOLVEDのまま、今回も未追跡）。

### 1.4 プリセット: `config_gamepad_priSet` / `GetGamePadPriSet`

`GetGamePadPriSet(Int32 priset_no)`のISILを直接確認:

```
rax = dds3ConfigGamePadSteam.staticfields
rdx = rax.config_gamepad_priSet          // offset 0x58, Int32[][]
if (priset_no < 0 || priset_no >= rdx.Length) priset_no = 0   // 負値/範囲外は0にクランプ
return rdx[priset_no]                    // Int32[] を返す（参照そのまま）
```

**CONFIRMED: `GetGamePadPriSet(priset_no)` は `dds3ConfigGamePadSteam.config_gamepad_priSet[priset_no]` を返すだけの単純アクセサ。** これは「プリセットA/B/C」の実体候補として非常に有力（フィールド名・アクセサの形が完全に一致）。ただし現時点で:
- `config_gamepad_priSet`の**要素数**（プリセットがいくつあるか、3固定かどうか）は未確認。
- 各プリセット配列の**中身の意味**（indexが何を表すか、`config_data[4]`と同じaction-id空間かどうか）は未確認。
これらはHYPOTHESIS/UNRESOLVEDとする。

### 1.5 `OnPreset` — プリセット選択UIハンドラ

```
OnPreset(Int32 type, SteamColliders mine, SteamCollider collider, Int32 index)
  if (type == 1) dds3ConfigGamePadSteam.arrowIndex = index;   // offset 0xC
  if (collider != null) {
      value = collider.field_0x18;      // collider側に紐付けられたタグ値（推定: プリセット番号）
      FUN_18228EAF0(type, value, 0, 0); // 新規シンボル、未解析
  }
```

**CONFIRMED**: `OnPreset`はタイトルConfig画面の「プリセット選択」に対応するUIコールバックであり、`arrowIndex`更新と`FUN_18228EAF0`呼出という2つの効果を持つ。`FUN_18228EAF0`（VA `0x18228EAF0`、`ChgConfigGamePadAll`のVA `0x18228E9B0`から+0xB40の近傍）は今回未decompileで、プリセット適用の実処理を担っている可能性が高い（STRONGLY SUPPORTED、名前根拠なし）。

---

## 2. Read path（GetConfigGamePad）— 最重要かつ最大の発見

`GetConfigGamePad(Int32 SIActionName_idx)`のISIL（約1400行）の先頭〜idx=5相当のcase分まで確認した結果、**予想に反して `config_data` を全く読んでいない**ことが判明した。

```
idx > 30 → return 0   (index 31-33が常にraw=0で返る、という既存A/Bテスト結果と一致)
idx 0..30 → 計算ジャンプテーブル（base = 0x182291300 + idx*4、Image Base 0x180000000相対）でcase分岐
```

idx=0,1,2は別の特殊経路（`[0x182E4EBA0]`アンカー → `.Instance+0x48` → `[0]` → `[4]` から定数寄りの値 9/11/12 を返すだけで、`rbx`(idx)に応じた分岐で固定値かplayer入力とは独立した値を返しているように見える）。

idx=3以降（switch各caseで個別にハードコードされたinner index）:

```
[0x182E4EBA0クラス].Instance          // +0xB8 (single deref)
  .field@0x48                        // Int32[][][] 的な入れ子構造（配列の配列の配列）
  [0]                                // 常に固定
  [4]                                // 常に固定（config_data側の「index 4 = gamepad」と符合）
  [innerIdx]                         // ← idxごとに個別にハードコードされた値、idx自体とは非線形
```

実際に確認できたマッピング（3件のみ、CONFIRMED）:

| SIActionName_idx | innerIdx |
|---|---|
| 3 | 3 |
| 4 | 7 |
| 5 | 8 |

**これは単純な「idxそのまま」ではない。** switch各caseに個別のinnerIdx定数が埋め込まれており、規則性は未確認（残り約26ケースを読めば全表が作れるが今回は未実施、§9参照）。

### 2.1 最重要の未解決点

`[0x182E4EBA0]`が指すクラスは、`dds3ConfigGamePadSteam`（`DAT_182e4e3b8`）でも`dds3GlobalWork`（`DAT_182e4ed30`）でもない**第三のクラス**（便宜上「Class B」と呼ぶ）。`ChangeKey`の冒頭にも同じ`DAT_182e4eba0`が登場するが、そこでは静的フィールド`+0x38`を単純な整数として読み、`config_data`の長さとの大小比較（サニティチェック）に使っているだけで、`GetConfigGamePad`が読む`+0x48`（入れ子配列）とは別のフィールド。

→ **CONFIRMED: `GetConfigGamePad`の読み取り先と`ChangeKey`/`ChangeKeyDuplicate`の書き込み先（`dds3GlobalWork.Instance.config_data[4]`）は、少なくとも直接には同一の配列ではない。** 両者の間には「Class B」という別のデータ層が挟まっており、そこにコピー/同期する処理がどこかに存在するはずである（HYPOTHESIS、根拠は次項）。

---

## 3. `ChgConfigGamePadAll()` — 同期処理の最有力候補（ただし未確定）

ISILでは0..33のループで:

- `dds3GlobalWork.Instance.config_data[4]`（34要素であることを毎回境界チェック）
- `dds3ConfigGamePadSteam`自身のクラス初期化チェック（`DAT_182e4e3b8`）

の両方に触れているが、**実際のデータコピー命令（値の読み書き）がISIL上に現れていない**。境界チェックのみがループ内で繰り返され、値そのものの移動が見当たらない。これはCpp2ILのISILリフターがベクトル化コピー（`movdqu`/`rep movsd`等）を正しく命令化できなかった既知の限界による欠落である可能性が高い（STRONGLY SUPPORTED、Cpp2ILの一般的な弱点として）。

**HYPOTHESIS（未検証）**: `ChgConfigGamePadAll()`は「`config_data[4]`（編集用バッファ、ChangeKeyが直接書く場所）の内容を、実際にgameplayが参照するランタイム構造（Class B、`GetConfigGamePad`が読む場所）へ一括コピーして適用する」関数である。これが正しければ、

```
config_data[4][actionId]  (persistent/edit buffer, ChangeKeyが直接書く)
        ↓ ChgConfigGamePadAll()  ← 未確認の同期処理
Class B の入れ子配列[0][4][innerIdx]  (GetConfigGamePadが読む、実際にgameplayで使われる"live"値)
```

という2段構成が「起動直後は34/34成功するがstale値、探索状態に入った後は正しい値になる」という既存の実機観測（SESSION_RESUME_NOTES.md §17-19）を自然に説明する。ただし**この一括コピーの実体は今回のCpp2IL ISILでは確認できておらず、UNRESOLVEDのまま**。

---

## 4. Preset path（プリセットA/B/C）

```
[CONFIRMED] dds3ConfigGamePadSteam.config_gamepad_priSet : Int32[][]  (静的フィールド, offset 0x58)
[CONFIRMED] GetGamePadPriSet(priset_no) => config_gamepad_priSet[priset_no]
[CONFIRMED] OnPreset(type, mine, collider, index) — プリセット選択UIのクリックハンドラ、arrowIndex更新 + FUN_18228EAF0(type, colliderの紐付け値, 0, 0)を呼ぶ
[UNRESOLVED] config_gamepad_priSet の要素数（A/B/C=3かどうか）
[UNRESOLVED] config_gamepad_priSet[n] の中身の意味（index空間がconfig_data[4]と同じ34要素action-id空間かどうか）
[UNRESOLVED] FUN_18228EAF0（VA 0x18228EAF0）の内部処理 — プリセット適用の本丸である可能性が高いが未decompile
[UNRESOLVED] プリセット選択結果がどこに永続化されるか（config_gamepad_priSet自体は「定義」であって「現在選択中のプリセット番号」を保持するフィールドではなさそうだが、それがどこにあるかは未特定）
```

---

## 5. Save/runtime relationship（保存側候補の再検討）

ユーザー提示の候補:
- `SAVEDATA.dds3GlobalWork_tag.config_data_gamepad`
- `dds3GlobalWork_t.config_data`

今回`dds3GlobalWork_t.config_data`（instance offset 0xE8, `Int32[][]`）の実在とwrite path一致をCONFIRMEDできた。`SAVEDATA.dds3GlobalWork_tag.config_data_gamepad`については今回**未調査**（`dds3GlobalWork_tag`という別クラス/構造体の存在有無、`config_data`との関係=同一メモリかコピーかは未確認、UNRESOLVED）。

`dds3GlobalWork_t`という名前自体が「グローバルワーク＝セーブ/ロード対象になりうるゲーム全体の作業領域」を示唆するため、`config_data`がセーブデータ側とも直接/間接に繋がっている可能性はSTRONGLY SUPPORTEDだが、シリアライズ経路そのものは未追跡。

---

## 6. Title Config UI relationship

`dds3ConfigGamePadSteam`自体が「Controller Key Config」画面のUIコントローラクラスであることはCONFIRMED（`Draw()`, `cfgDrawFrame`, `cfgDrawCaption`等の描画メソッド、`OnPreset`/`OnAdjustment`/`OnMinMax`というUIコライダーコールバック群、`EnterCheck`/`selectNG`/`IsKeyChangeMode`等のUI状態管理メソッドが同一クラスに同居）。

`Init()`（994行目〜）は未読。「画面に入った時に何をロードするか」を追う最有力候補として次回最優先。

---

## 7. Function table（今回判明した新規シンボルを含む）

| Symbol | 型 | 備考 |
|---|---|---|
| `ChangeKey` | RVA 0x228E320 | CONFIRMED（前回セッション） |
| `ChangeKeyDuplicate` | RVA 0x228DEA0 | CONFIRMED（前回セッション） |
| `FUN_182296ef0` | 未登録RVA（前回セッション判明） | CONFIRMED: 実際の1要素書込み本体 |
| `FUN_1822ee830` | 未登録RVA（前回セッション判明） | UNRESOLVED: 全パス末尾の共通呼出、`FUN_1822ee540(1,0,0)`を呼ぶだけ |
| `GetGamePadPriSet` | 未登録RVA（今回、ISILのみ） | CONFIRMED: `config_gamepad_priSet[priset_no]`のアクセサ |
| `OnPreset` | 未登録RVA（今回、ISILのみ） | CONFIRMED: プリセット選択UIハンドラ、`FUN_18228EAF0`を呼ぶ |
| `FUN_18228EAF0` | VA 0x18228EAF0（`OnPreset`からのcall先として今回判明） | **UNRESOLVED、新規、次回最優先候補** |
| `GetConfigGamePad` | 未登録RVA（今回、ISILのみ） | CONFIRMED: `[0x182E4EBA0]`クラス（Class B）経由で読む。`config_data`は読まない |
| `ChgConfigGamePadAll` | RVA 0x228E9B0（既知） | STRONGLY SUPPORTED: config_data⇄Class Bの同期処理と推定されるが実コピー命令は今回未確認 |
| `ChgConfigGamePad(uint,int)` | RVA未特定 | UNRESOLVED: Cpp2IL cs上は本体が`Return`のみ（stub/extern扱いの可能性、要再確認） |
| Class B（`DAT_182e4eba0`） | VA 0x182E4EBA0（グローバル変数） | **UNRESOLVED、新規、クラス名未特定。次回最優先候補** |

---

## 8. データフロー図（現時点で埋まっている範囲）

```
[config_gamepad_priSet: Int32[][]]  (CONFIRMED実在、静的フィールド 0x58)
        ↓ GetGamePadPriSet(n) [CONFIRMED]
        ↓ OnPreset UI handler → FUN_18228EAF0 [UNRESOLVED本体]
        ↓ ???
[dds3GlobalWork.Instance.config_data: Int32[][]]  (CONFIRMED実在, instance 0xE8)
   ← ChangeKey/ChangeKeyDuplicate/FUN_182296ef0 が config_data[4][actionId] へ直接書込み [CONFIRMED]
        ↓ ChgConfigGamePadAll() [STRONGLY SUPPORTED、実コピー命令は未確認]
[Class B (DAT_182e4eba0) .Instance.field@0x48 : 入れ子配列]  (CONFIRMED実在、read専用で確認)
        ↓ GetConfigGamePad(idx) が [0][4][idxごとのハードコードinnerIdx] を返す [CONFIRMED]
```

write側（将来のMOD write用、今回は提案のみ・未実装）:

```
Settings future write
        ↓ ???
ChangeKey / ChangeKeyDuplicate（native呼出、今回未実施）
        ↓
config_data[4][actionId] 更新
        ↓ ChgConfigGamePadAll()（同期、未確認）
        ↓
Class Bのruntime構造も更新される「はず」（未検証）
        ↓
save/apply（未追跡）
```

---

## 9. Open questions（次回最優先）

1. **Class B（`0x182E4EBA0`）の正体**: どのC#クラスか（`Init()`同様、Cpp2ILの他クラスファイルからこのグローバル変数を参照するクラスを逆引きする、またはGhidraで`0x182E4EBA0`のxrefを見るのが早い）。
2. **`GetConfigGamePad`の全idx→innerIdxマッピング表**（3件のみ判明、idx=6以降 約26件が未確認。ISIL 3809〜5245行を全部読めば機械的に埋まる）。
3. **`ChgConfigGamePadAll`の実コピー命令**をGhidra decompileで確認（Cpp2IL ISILの欠落を補う）。
4. **`FUN_18228EAF0`**（`OnPreset`のcall先）のdecompile。プリセット適用の実体である可能性が高い。
5. **`config_gamepad_priSet`の要素数と中身**（実機で3プリセット分か確認、各プリセットの34要素相当データをdumpして比較）。
6. **`Init()`**（994〜1104行目、config画面表示開始時の処理）— セーブデータからのロード地点である可能性。

---

## 10. Recommended next experiment（READ-ONLY、今回は提案のみ・未実装）

静的解析だけでは`config_data`と`Class B`の同期タイミング/条件が確定しないため、以下の安全なread-only実機テストを提案する（テストコード実装は今回行っていない）:

1. 純正Controller Key Config画面でプリセットA→B→Cと切り替える。
2. 各プリセット選択直後（画面はまだ開いたまま、「決定」で確定する前）に、既存`GameActionBindingSnapshotReader.Capture`相当のread-onlyダンプ（`GetConfigGamePad(0..33)`）を取得。
3. 同時に、もし安全にreflectionで読めるなら`dds3GlobalWork.Instance.config_data[4]`（34要素）も並行ダンプ。
4. 「決定」で確定させた後にもう一度両方dump。
5. A/B/Cそれぞれの`GetConfigGamePad`結果と`config_data[4]`結果を比較し、
   - プリセット切替直後に`GetConfigGamePad`だけが変わるか（Class Bが即座に更新される＝プレビュー用に別経路で即時反映される設計）、
   - それとも「決定」を押すまでは両方とも変わらないか、
   - `config_data[4]`が「決定」時点で初めて更新されるか、
   を判定する。

この結果によって、「プリセット選択→即時プレビュー反映か、確定時のみ反映か」「`config_data`が最終確定値のみを保持するのか、プレビュー中の値も持つのか」という残りの疑問が解ける見込み。

---

## 11. 追補（2026-09-22 続き）— Class B特定 + ground-truth disassembly検証

前回の指示に従い、Class B特定を最優先で実施。加えてユーザーの警告どおり「Cpp2IL ISILにcopy命令が見えない」だけを根拠にせず、`GameAssembly.dll`から直接capstoneでraw disassembly（pefile+capstone、image_base=0x180000000）してground truthを取った。**今回もGhidra Project本体は開いていない**（GUIアクセスなし）。native呼出・write・build・deploy・commit等は一切行っていない。

### 11.1 Class B の正体（CONFIRMED）

`0x182E4EBA0`を参照する全`.txt`(IsilDump)をファイル単位で集計したところ、`dds3ConfigMainSteam.txt`が289件で突出（2位はdds3ConfigGamePadSteam自身の123件、これは他クラスの公開static fieldへの外部参照分）。

決定打: `dds3ConfigMainSteam.AddCollider()`冒頭で、**このメソッド自身の**class-init-check（自クラスのinit-flag `[0x182E2CB96]`、自クラスのcctor thunk引数 `[0x182ABFFD0]`）の直後に`rax=[0x182E4EBA0]`→`rax=[rax+184(0xB8)]`という自己static-fields取得が続く。これは他クラスの自己参照パターン（例: `dds3ConfigGamePadSteam`が`0x182E4E3B8`を自分のメソッド内で同じ形で使う）と完全に同一の型。

**CONFIRMED: Class B (`0x182E4EBA0`) = `dds3ConfigMainSteam`。**

`dds3ConfigMainSteam`は「Controller Key Config」を含むConfig画面全体を統括する`static`クラスで、`dds3ConfigGamePadSteam`/`dds3ConfigKeyBoardSteam`/`dds3ConfigDisplaySteam`/`dds3ConfigAudioSteam`/`dds3ConfigGraphicsSteam`/`dds3ConfigGameSteam`という各タブ用クラスの「親/ハブ」に相当する（Cpp2IL C#ダンプのフィールド一覧に`configUI configUIScrSteam`、`TabStr`、`ConfigCaptionData`、`SelText`系フィールドが多数存在し、UI文字列・タブ管理を担うことと整合）。

### 11.2 field +0x48 の正体（矛盾あり、UNRESOLVED）

Cpp2IL diffable C#ダンプ上、`dds3ConfigMainSteam`のoffset 0x48は:

```
public static String[][] SelText; //Field offset: 0x48
```

しかし`GetConfigGamePad`のidx=3ケースで確認した実際のアクセス連鎖は:

```
staticFields+0x48 → [0](8byteストライド) → [4](8byteストライド) → [innerIdx](4byteストライドで境界チェック→値read)
```

**最後の1段だけ4byteストライド**（Int32要素）であり、`String[][]`（参照型要素、常に8byteストライド、かつ2階層のみ）とは構造的に矛盾する。3階層目まで存在し、かつ最終段がInt32相当というのは`SelText`の宣言型と合わない。

**この矛盾はUNRESOLVEDのまま報告する。** 考えられる原因（いずれも未検証）:
1. Cpp2ILのfield offset/型推定がこのクラスについて不正確（複雑な static class で稀に起こりうる）。
2. 今回参照したCpp2ILダンプの生成元`GameAssembly.dll`が、`SMT3HD_GameAssembly_METADATA.md`にpinされたSHA-256と異なるビルドである可能性（今回未検証、生成元バイナリの版一致は未確認）。
3. 単純な解析ミス（可能性は低いと考えるが、Ghidra native decompileでの直接検証が必要）。

**次回はGetConfigGamePadを直接Ghidra decompileし、staticFields+0x48の実際の型/フィールド名をGhidra側のIL2CPP型情報から確定させること。** 今回はGhidra GUI/headlessへのアクセスがなく、これ以上の解決は持ち越し。

### 11.3 `ChgConfigGamePadAll()` の実体（CONFIRMED、raw disassembly、ISIL疑義を解消）

`ChgConfigGamePadAll`(VA `0x18228E9B0`〜`0x18228EA6E`)をcapstoneで全命令disassembleした。**binding payloadの値をコピー・更新するread/writeは存在しない**（`config_data[4]`のnull/length/bounds確認のためのreadは行う）。

やっていることは:
1. `dds3GlobalWork.Instance.config_data`のnullチェック・長さチェック（`>4`であることの確認のみ）。
2. `config_data[4]`（`r8`）の長さが現在のループindex(`ebx`, 0..0x21)より大きいことの境界チェックのみ（`cmp ebx,[r8+0x18]; jae throw`）。
3. `dds3ConfigGamePadSteam`のクラス初期化チェック（未初期化ならcctor実行）。
4. `ebx`をインクリメントして0x22(34)になるまでループ。

**結論（CONFIRMED、ground truth、表現修正版）: `ChgConfigGamePadAll()`は`config_data[4]`のnull/length/bounds確認のためのreadは行うが、binding payloadの値をコピー・更新するread/writeは存在しない。**「`config_data` → Class B への同期処理」という前回のSTRONGLY SUPPORTED仮説は、少なくともこの関数に関しては**反証(FALSIFIED)**された。同期処理は別の場所にあるはずである（UNRESOLVED、次点調査対象）。

### 11.4 `FUN_18228EAF0`（`OnPreset`の呼出先）の実体（CONFIRMED、raw disassembly）

`FUN_18228EAF0`(VA `0x18228EAF0`〜`0x18228EB80`、および分岐先`0x18228EB81`〜`0x18228EC1C`)を全命令disassembleした。

引数: `(int type, <object> rsi_arg, int index)`（`OnPreset`からは`type`, `collider.field+0x18`(タグ値), `0`が渡される — 実質`index=0`固定でOnPresetからは呼ばれている）。

内部で参照するアンカーは`[rip相対]`計算により **`0x182E4E3B8`** = **`dds3ConfigGamePadSteam`自身**のstatic-fields anchorであると判明（Class Bとは別、`dds3ConfigGamePadSteam`本体）。使用フィールドは:

- `+0x18` = `mEnter`（`Boolean[]`、フィールド表§1.1と一致）
- `+0x20` = `mListName`（`String[]`、フィールド表§1.1と一致）

処理内容:

```
if (type != 1):
    dds3ConfigGamePadSteam.mEnter[index] = false        // ハイライト解除
else (type == 1):
    matched = StringCompareFn(rsi_arg, dds3ConfigGamePadSteam.mListName[index])  // call 0x181496630
    if (matched):
        dds3ConfigGamePadSteam.mEnter[index] = true      // ハイライト設定
```

**結論（CONFIRMED）: `FUN_18228EAF0`はconfig_dataや実際のbinding値には一切触れない。これは「リストのどの項目が選択/ホバーされているか」を`mEnter`配列でトラッキングする汎用UIハイライト更新関数であり、`dds3ConfigMainSteam`の`OnChoiceCollider`/`OnConfigCursorMove`/`OnSpaceCollider`等、他の`On*Collider`ハンドラからも同種の目的で使われている可能性が高い（STRONGLY SUPPORTED、命名パターンと引数形状から）。**

→ **「プリセット選択時に実際の34アクション分の値を書き込む処理」は、`OnPreset`の直接の呼び出し先からは見つからなかった。** プリセット確定時に別途`ChangeKey`/`ChangeKeyDuplicate`をループ呼出する経路、または未特定の別関数が存在するはずである（UNRESOLVED、次回優先調査対象）。

### 11.5 更新後のEvidence一覧（§7を上書きする差分のみ）

| 項目 | 旧評価 | 新評価 |
|---|---|---|
| Class B の正体 | UNRESOLVED | **CONFIRMED: `dds3ConfigMainSteam`** |
| `ChgConfigGamePadAll`=config_data→Class B同期 | STRONGLY SUPPORTED | **FALSIFIED（該当関数はコピーを行わない、CONFIRMED by raw disasm）** |
| `FUN_18228EAF0`=プリセット適用処理 | UNRESOLVED（有力候補） | **FALSIFIED（UIハイライト更新のみ、CONFIRMED by raw disasm）。プリセット適用の実体は依然UNRESOLVED** |
| field+0x48の型 | （前回は未検証） | **矛盾あり、UNRESOLVED**（Cpp2IL上`SelText:String[][]`だが実アクセスは3階層Int32終端） |

### 11.6 改訂版データフロー図

```
[config_gamepad_priSet: Int32[][]]  (CONFIRMED実在)
        ↓ GetGamePadPriSet(n) [CONFIRMED]
        ↓ OnPreset → FUN_18228EAF0 [CONFIRMED: UIハイライト更新のみ、値は書かない]
        ↓ ??? [UNRESOLVED — プリセット確定時の実際の適用経路は未発見]

[dds3GlobalWork.Instance.config_data: Int32[][]]  (CONFIRMED実在)
   ← ChangeKey/ChangeKeyDuplicate/FUN_182296ef0 が config_data[4][actionId] へ直接書込み [CONFIRMED]
        ↓ ChgConfigGamePadAll() [FALSIFIED: 同期処理ではない、純粋な境界検証のみ]
        ↓ ??? [UNRESOLVED — config_data→Class Bへの実際の反映経路は未発見]

[dds3ConfigMainSteam (=Class B) の static field @0x48]  (CONFIRMED実在、型はUNRESOLVED)
        ↓ GetConfigGamePad(idx) が [0][4][idxごとのハードコードinnerIdx] を返す [CONFIRMED]
```

**現時点での最大の未解決点は変わらず「config_data ⇄ Class B間の同期処理をどの関数が担っているか」だが、少なくとも`ChgConfigGamePadAll`と`FUN_18228EAF0`という2つの有力候補が明確に除外できたことは大きな前進。**

### 11.7 補足: 「3/26件」表記について

前回報告の「GetConfigGamePadの全idx対応表（3/26件判明）」は正確には: `GetConfigGamePad`はidx 0..30の31ケースをジャンプテーブルで個別処理し（idx 31-33は即座に0を返す、既存A/Bテストと一致）、うちidx=0,1,2は別経路（本追補で調査対象外）、idx=3,4,5の3件のみ具体的なinnerIdx値を確認済み、残り26件（idx=6..30）が未確認、という意味。34slotのうち26個しかmapped actionがない、という意味ではない（記載が曖昧だった点を訂正）。

## 12. 追補2（2026-09-22 続き2）— read source型確定の試み + sync関数探索

指示どおり、まずCpp2ILダンプとcanonical binaryの版一致確認、次にGetConfigGamePadの完全ground truth解析、Class Bの再確認、sync関数探索の順で実施。**引き続きANALYSIS ONLY、native呼出・write・build・deploy・commit等は一切行っていない。**

### 12.1 Cpp2ILダンプとcanonical binaryの版一致確認（結果: 一致するが、型/呼出先情報は信頼できない）

`GameAssembly.dll`の現在のSHA-256を再計算:

```
59adbb5b18aaedc7df6c1672790ca48be99b5e8559c81db27132c436fb0a9fc4
```

`SMT3HD_GameAssembly_METADATA.md`にpinされた値（`59ADBB5B18AAEDC7DF6C1672790CA48BE99B5E8559C81DB27132C436FB0A9FC4`）と大文字小文字を除き**完全一致**。

**Evidence分類（訂正版、対象を明確に分離する）:**
```
CONFIRMED:
現在解析中のGameAssembly.dll = canonical Ghidra対象バイナリ
（SHA-256一致で確認）

UNRESOLVED:
既存のCpp2ILダンプ（NocturneModernGameplay/.analysis配下）が
その同一バイナリから生成されたことそのもの
（生成ログ・ハッシュ記録等の直接証拠は見つかっていない）
```

Cpp2ILダンプ生成元の一致は確認できていないが、§12.2/12.4で示す通り**同一バイナリを直接raw disassemblyした結果とCpp2IL情報の間に複数の不一致が見つかった**（offsetパターン自体は概ね一致するが、field型名と一部の呼出先アドレスが信頼できない）。この不一致自体が「Cpp2IL情報をそのまま信用すべきでない」根拠として、生成元バイナリの一致有無に関わらず成立する。

**結論: 今回の指示どおり、Cpp2ILの型情報・呼出先アドレスは補助証拠に格下げし、native disassembly（capstone、image_base=0x180000000、直接pefileでオフセット解決）をground truthとして扱う。** 以降の§12.2〜12.4は全てraw disassemblyによる直接検証。

### 12.2 GetConfigGamePadの完全ground truth解析（重要な訂正あり）

`GetConfigGamePad`のジャンプテーブル（RVA `0x2291300`、`rdx=image_base; entry=[rdx+0x2291300+idx*4]; target=entry+rdx`、ISILの記述通りCONFIRMED）を直接バイト読みし、idx 0〜9のジャンプ先を解決した:

| idx | jump target |
|---|---|
| 0,1,2,3 | `0x18228FCC0`（**4つとも同一アドレス、共有ブロック**） |
| 4 | `0x18228FFA3` |
| 5 | `0x182290020` |
| 6 | `0x18229009D` |
| 7 | `0x182290110`(推定、未確認) |
| 8 | `0x182290197` |
| 9 | `0x182290214` |

**重要な訂正（前回§2の誤りを修正）: idx=3は独立したケースではなく、idx=0,1,2と同じ共有ブロック（`0x18228FCC0`）に属する。** 前回「idx=3→innerIdx=3」とした対応は**誤り**。raw disassemblyで確認した実際の動作:

```
(dds3ConfigMainSteam.staticFields+0x48 → [0] → [4] を読み、
 その配列の要素[1]（+0x24オフセット）が値2と等しいかを判定する分岐の後)
if (idx==0) return 12 (0xC)
if (idx==1) return 9
if (idx==2) return 11 (0xB)
if (idx==3) return 10   // eax = ebx+9 (ebx=idx-2=1のとき) の計算結果
else (上記条件を満たさない場合、別の分岐先で同様の0/1/2/3カスケードが別の定数群で存在。今回は追跡未了)
```

つまり **idx 0,1,2,3の4つ全てが「configテーブルの単純な要素読み出し」ではなく、`dds3ConfigMainSteam+0x48[0][4]`配列の`[1]`要素の値によって分岐する、ハードコードされた定数（または軽い計算）を返す特殊ブロック**である（CONFIRMED、raw disassembly）。この4件は「34アクション分のGAME binding値」の一部ではない可能性が高い（STRONGLY SUPPORTED — 名前的にもUI寄りの用途と推測されるが、確定はしていない）。

一方、**idx=4, idx=5は独立したジャンプテーブルエントリを持ち、以下の経路が完全にCONFIRMED（raw disassembly、byte-exact）**:

```
idx=4:
  classPtr = [rip相対、resolve先=0x182E4EBA0 (= dds3ConfigMainSteam、§12.3で再確認)]
  staticFields = [classPtr+0xB8]
  arr0 = [staticFields+0x48]         ; null/length(>0)チェック
  arr1 = [arr0+0x20]                 ; = arr0[0], null/length(>4)チェック
  arr2 = [arr1+0x40]                 ; = arr1[4], null/length(>7)チェック
  return *(int*)(arr2+0x3C)          ; = arr2[7]  ← 4byteストライド、Int32要素であることが確定

idx=5:
  (同一パターン)
  arr2 = [arr1+0x40]                 ; = arr1[4]
  return *(int*)(arr2+0x40)          ; = arr2[8]  ← 4byteストライド
```

**CONFIRMED（byte-exact）: idx=4→内部index7、idx=5→内部index8。前回のISILベース報告と一致（この2件は正しかった）。**

**field+0x48の型についての結論**: raw disassemblyで`[classPtr+0xB8][+0x48][0][4][innerIdx]`という3階層＋最終4byteストライド読み出しが確定した。これは`Int32[][][]`的な構造でなければ成立しない。Cpp2ILが同フィールドを`String[][]`（2階層、8byteストライド、String要素）と報告しているのは**offsetの指し先は合っているが型名/階層情報が実際のnativeコードと矛盾している（CONFIRMED不一致）**。原因はCpp2ILの型推定誤りと判断するのが最も妥当（§12.1参照、呼出先アドレスでも同様の不整合を確認したため）。**正式なfield名は依然UNRESOLVED**（Ghidraでこのクラスの完全な型情報を見るか、global-metadata.datから直接fieldテーブルを再構築する必要がある）。

### 12.3 Class B = dds3ConfigMainSteamの再確認（IL2CPP構造の明示）

`0x182E4EBA0`（および同一クラスを指す別キャッシュスロット`0x182E4E410`、`0x182E4E3B8`とは別物）は、命令列から次のように明確に区別できる:

```
[0x182E4EBA0] という「グローバル変数スロット」は、
  → dds3ConfigMainSteamのIl2CppClass*（クラスメタデータへのポインタ）を保持するキャッシュセル
  → 初回アクセス時にil2cpp_runtime_class_init等で解決され、以後このグローバルスロットにキャッシュされる
    （このパターンは全クラス共通、"test byte ptr [X+0x12F],2"がinit済みフラグチェック）

Il2CppClass* → +0xB8 (184) → static_fields実体へのポインタ（一段の参照外し）
  → dds3ConfigGamePadSteam/dds3GlobalWork/dds3ConfigMainSteamいずれも同一offset 0xB8で
    static fieldsを得ている（CONFIRMED、複数クラスで再現）

static_fields + fieldOffset → 各static fieldの値（プリミティブ型は直接、参照型はオブジェクトポインタ）
```

**明確化: `0x182E4EBA0`自体は「Il2CppClassへのグローバルキャッシュポインタ」であり、「static fields anchor」そのものではない。** 「static fields」は`[0x182E4EBA0]`を一段dereferenceして`+0xB8`を読むことで得られる別のポインタである。前回の報告でこの2つを明確に区別していなかった点を訂正する。

`dds3GlobalWork`の場合はさらに一段深く、`[anchor]→+0xB8(static fields)→[0](=Instance singleton参照、reference型static fieldの中身)→+0xE8(=config_data)`という二段の参照外しが必要（Instanceパターン）。`dds3ConfigMainSteam`は全fieldが`static`（Cpp2IL C#ダンプで確認済み、Instanceプロパティなし）なので、`[anchor]→+0xB8→+fieldOffset`の一段で済む。この違いは前回から一貫して観察されており、CONFIRMEDのまま。

### 12.4 真のsync/apply関数探索 — 有力候補と、その呼出先が信頼できないという新知見

`dds3ConfigMainSteam`のCpp2IL C#ダンプに、まさに探していたシグネチャのメソッド群が存在する:

```
public static void ConfigAllDateInit(Boolean backupinit = True)
public static void ConfigAllDateInit2(ref Int32[][] config_data, Boolean backupinit = False)
public static void ConfigAllDateInit2Tab(Int32 tabpos, ref Int32[][] config_data, Boolean backupinit = False)
public static Int32[] GetConfigInitTable(Int32 tabpos)
private static void DatePrev(Int32 tabpos)
private static void DefaultPrev(Int32 tabpos)
public static void DateSave()
```

`ConfigAllDateInit()`のISILを確認したところ:

```
rax = dds3GlobalWork.staticFields[0]      // Instance singleton (offset 0、二段目の参照外し前の生ポインタ)
rbx = &(Instance + 0xE8)                   // = &config_data （アドレスそのもの、ref引数として渡す準備）
call FUN_1822B9E90(rcx=rbx(&config_data), rdx=backupinit, r8=0)
```

**これは`ConfigAllDateInit2(ref config_data, backupinit)`の実体呼出しそのものに見える（`dds3GlobalWork.Instance.config_data`への参照を直接渡している）。構造的にはSTRONGLY SUPPORTEDな「sync関数」候補。**

**しかし、指示に従いこの呼出先`0x1822B9E90`を実際にraw disassemblyしたところ、最初の命令が別領域`0x1965597F0`への`jmp`であり、その先は通常のIL2CPPコード生成パターンとは全く異なる、レジスタ演算を大量に積み重ねるコード（VMProtect等の保護機構、またはSteamworks DRM関連コードに酷似）だった。**

**結論（表現修正版）:**
```
CONFIRMED:
Cpp2ILが示す0x1822B9E90は通常のIL2CPP本体らしい形ではない。

UNRESOLVED:
それが誤解決（Cpp2ILのcall target解決ミス）なのか、
保護thunk/dispatcherを介して正規のConfigAllDateInit2本体へ
戻る正規経路なのかは、まだ除外できていない。
```
「実処理本体ではありえない」と断定するのは言い過ぎであり、上記の通り訂正する。§12.1で示した通りCpp2ILの型/アドレス情報は補助証拠に格下げする方針が、ここでも裏付けられた。

**「`ConfigAllDateInit`が`config_data`への参照をどこかへ渡している」という構造自体はISIL上明確だが、その先で実際に何が行われるかは依然UNRESOLVED。** これを解決するには、`ConfigAllDateInit`自体の実アドレス（Cpp2ILの呼出先ではなく、このメソッド自身の開始VA）をraw disassemblyで特定し、その中の`call`命令が実際にどこを指しているかをbyte-exactに読む必要がある（今回はConfigAllDateInit自身の開始VAの特定までは至らず、時間の都合で次回送り）。

### 12.5 GetGamePadPriSet() xref / config_gamepad_priSetの中身

今回は未着手（時間配分の都合でClass B特定・read source型確認・sync関数探索を優先したため）。次回優先課題として持ち越し。

### 12.6 更新後のEvidence一覧（§7・§11.5への追加差分）

| 項目 | 評価 |
|---|---|
| Cpp2ILの型/呼出先アドレス情報の信頼性 | **格下げ（補助証拠のみ）— 実際にraw disassemblyと2件（field型、call先）で不一致を確認** |
| GetConfigGamePad idx=3の扱い | **訂正: idx=0,1,2と同じ特殊ブロック（定数/計算値を返す）。前回の「innerIdx=3」は誤り** |
| GetConfigGamePad idx=4→7, idx=5→8 | CONFIRMED（byte-exact raw disassemblyで再確認、変更なし） |
| dds3ConfigMainSteam+0x48の型 | CONFIRMED不一致（Cpp2IL: String[][]、実際: 3階層Int32終端構造）。正式field名は依然UNRESOLVED |
| `0x182E4EBA0`の正確な意味 | 明確化: Il2CppClassへのグローバルキャッシュポインタ（static fields anchorそのものではない、+0xB8を挟んで得る） |
| `ConfigAllDateInit`→sync関数 | STRONGLY SUPPORTED（構造的にconfig_data参照を下流へ渡している）だが、**Cpp2IL報告の呼出先アドレスはFALSIFIED（別領域の難読化コードに着地）**。真の呼出先は依然UNRESOLVED |

### 12.7 次回への申し送り

優先順位（変更なし、今回完了できなかった分）:

1. `ConfigAllDateInit`自身の開始VAをraw disassemblyで特定し、内部の`call`命令の実際の呼出先をbyte-exactに確認する（Cpp2IL経由の`0x1822B9E90`は使わない）。
2. `dds3ConfigMainSteam+0x48`の正式field名をglobal-metadata.datのfieldテーブルから直接引く（`metadata_parse.py`と同様の手法をdds3ConfigMainSteamのtypeDefinitionに対して適用すれば機械的に可能）。
3. `GetGamePadPriSet()`のxref（呼び出し元）を洗い出し、「取得したInt32[]をどこへ渡しているか」を追う。
4. `config_gamepad_priSet`の中身（要素数・各presetの値）を静的抽出。

## 13. 追補3（2026-09-22 続き3）— native VA確定 + プリセット適用機構の発見

指示どおりEvidence表現を2点訂正（§12.1、§12.4）した上で、以下3点に集中した。**引き続きANALYSIS ONLY、native呼出・write・build・deploy・commit等は一切行っていない。**

### 13.0 手法: Cpp2ILに依存しないVA解決ツールの発見・活用

`NocturneModernGameplay/.analysis/resolve_name_to_va.py`という、以前の別セッションが作成済みのツールを発見した。`global-metadata.dat`の`Il2CppTypeDefinition`/`Il2CppMethodDefinition`テーブルと、`GameAssembly.dll`内の`Il2CppCodeGenModule`（`methodPointers`配列）を直接読み、**Cpp2ILを一切経由せず**C#の`型名.メソッド名`からnative VAを機械的に解決する。これはIl2CppDumper/Cpp2IL自身が内部で使うのと同種の手法を独立実装したものであり、Cpp2ILより信頼できるground truthとして採用した。

**ツールの正当性検証（sanity check）**: 既にGhidra decompile/前回raw disassemblyで確定済みの3件のVAと照合し、**3/3で完全一致**を確認した:

| メソッド | 既知VA（Ghidra/METADATA.md） | ツール解決結果 |
|---|---|---|
| `dds3ConfigGamePadSteam.ChangeKey` | `0x18228E320` | `0x18228E320` ✅ |
| `dds3ConfigGamePadSteam.ChgConfigGamePadAll` | `0x18228E9B0` | `0x18228E9B0` ✅ |
| `dds3ConfigGamePadSteam.ChangeKeyDuplicate` | `0x18228DEA0` | `0x18228DEA0` ✅ |

**このツールをCONFIRMEDなground truthとして採用する。**

### 13.1 タスク1: ConfigAllDateInit()の真のnative開始VA（CONFIRMED）

```
dds3ConfigMainSteam.ConfigAllDateInit(Boolean backupinit=True)
  -> VA = 0x1822BA0C0

dds3ConfigMainSteam.ConfigAllDateInit2(ref Int32[][] config_data, Boolean backupinit=False)
  -> VA = 0x1822B9E90   ← Cpp2ILが「ConfigAllDateInitの呼出先」として報告していたアドレスと完全一致
```

**重要な訂正: 前回「Cpp2ILの呼出先0x1822B9E90は信頼できない」としたのは誤りだった。** metadata経由のground truth解決で、この`0x1822B9E90`は実在する`ConfigAllDateInit2`の正しい開始VAであることがCONFIRMEDされた。

`0x1822BA0C0`（`ConfigAllDateInit`本体）をraw disassemblyし、先頭部分がISILで読んだ内容（`dds3GlobalWork`のstatic fields→offset0→Instance→`&(Instance+0xE8)`=`&config_data`を計算する処理）と一致することを確認した（CONFIRMED）。

**新たな確定事実: `ConfigAllDateInit2`（VA `0x1822B9E90`）の実際の命令列は、1バイト目から`jmp 0x1965597F0`という無条件ジャンプであり、その先は通常のIL2CPPコード生成パターンとは全く異なる、大量のレジスタ演算を積み重ねる難読化されたコード領域だった。**

```
CONFIRMED:
ConfigAllDateInit2の実開始VAは0x1822B9E90であり、
これはCpp2ILの報告と一致する（Cpp2ILの誤りではない）。
このVAの最初の命令は別領域(0x1965597F0)への無条件jmpであり、
その先は通常のIL2CPPコード生成形状とは異なる
（VMProtect等の保護機構、またはSteamworks DRM関連コードに酷似）。

UNRESOLVED:
この保護/難読化が具体的に何であるか、
どうやってそれを読み解くか（今回のツールセットでは対応不可）。
config_data実データがどう扱われているかは、
この関数の内部までは追えなかった。
```

**この関数（`ConfigAllDateInit2`）自体の内部をこれ以上raw disassemblyで追うのは、今回の手段では不可能と判断する。** 深追いは行わなかった。

### 13.2 タスク2: +0x48正式field名（部分的に解決、型は依然矛盾）

`global-metadata.dat`から`dds3ConfigMainSteam`のfield一覧を直接再構築した（74フィールド、fieldIndex 14008〜14081）。Cpp2ILの診断的C#ダンプが報告していたfield順序・名前と**完全に同じ並び**であることを確認した（16番目、fieldIndex=14023、`name='SelText'`、Cpp2ILの`SelText: String[][] //Field offset: 0x48`と同一ポジション）。

```
CONFIRMED:
native offset +0x48に対応するfieldは、
metadata上も"SelText"という名前で登録されている
（Cpp2ILの命名は誤りではなく、metadataと一致）。

UNRESOLVED（依然解消せず）:
metadataのfield定義はtypeIndex=36857という
型参照を持つが、これをIl2CppType blobまで
デコードして実際の型（要素型・階層数）を
確定するところまでは今回至っていない。
raw disassemblyで確認した3階層Int32終端構造と、
"String[][]"という表面上の型注釈との矛盾は未解消。
```

参考: 同じくCONFIRMED済みの`config_allInitdate`（dds3ConfigMainSteam、native offset 0x1B0、`GetConfigInitTable`経由でInt32[][]であることが§13相当のraw disassemblyで裏付け済み）のtypeIndexは36833であり、SelTextのtypeIndex(36857)と近い範囲にある。これは示唆的だが、型の同一性を証明するものではない（HYPOTHESISに留める）。

### 13.3 タスク3: GetGamePadPriSet()のnative xref — 重大な発見

`GetGamePadPriSet`の真のVAは`0x182291710`（CONFIRMED、metadata解決）。

**これは、既に前回セッションでdecompile済みだった`FUN_182296ef0`（VA `0x182296ef0`）の内部から呼ばれていたコードと同一アドレスであることが判明した。** `FUN_182296ef0`のdecompile文面（`investigations/ghidra/FUN_182296EF0_decompile.txt`、既存資産）を読み直したところ、次の処理が確認できる（**このdecompileは前回セッションでGhidra GUIから取得済みのものを再利用しただけで、今回新規にnative呼出等は一切行っていない**）:

```c
// FUN_182296ef0のcase 0/1（param_1 ∈ {0,1}のとき）:
iVar2 = clamp(param_3, 0, 4);           // param_3=要求されたpreset番号を0..4にクランプ
lVar3 = *param_2;                       // = config_data[4]（呼出元から渡された配列）
*(int*)(lVar3 + 0x20 + param_1*4) = iVar2;   // config_data[4][0 or 1] = クランプ済みpreset番号
if (param_4 != 0) {                     // applyFlag（ChangeKey/ChangeKeyDuplicateは常に1を渡す）
    lVar3 = FUN_182291710(iVar2, 0);    // = GetGamePadPriSet(presetNumber) ★ここ★
    for (uVar5 = 0; uVar5 < 0x22; uVar5++) {
        if (uVar5 > 2) {                // index 0,1,2はスキップ
            lVar1 = *param_2;           // = config_data[4] （再取得）
            *(int*)(lVar1+0x20+uVar5*4) = *(int*)(lVar3+0x20+uVar5*4);
            // config_data[4][uVar5] = config_gamepad_priSet[presetNumber][uVar5]
        }
    }
}
```

```
CONFIRMED（既存decompileの再解釈のみ、native呼出なし）:

FUN_182296ef0のcase 0/1は、
「config_data[4][0 or 1]へpreset番号を書き込み、
 かつ applyFlag!=0の場合はGetGamePadPriSet(presetNumber)で
 取得したInt32[]の要素[3..33]を丸ごとconfig_data[4]へ
 コピーする」処理である。

これはまさに探していた
「config_gamepad_priSet[preset] → config_data[4]」への**Preset Apply Path（プリセット一括適用処理）**そのものである。

**用語整理（重要）**: 今回CONFIRMEDしたのはあくまで「Preset Apply Path」（プリセット定義→編集/保存側`config_data[4]`への適用）であり、「Runtime Apply/Sync Path」（`config_data[4]`→`GetConfigGamePad`が読むruntime側構造への反映）とは別物として区別する。両者を「同期関数」と一括りにしない。
```

**これで「保存/編集側」の内部（`config_data[4]`へのpreset一括適用）は完全に解けた。** ただし、この経路が実際にどう機能するかについては注意点がある:

- `ChangeKey`/`ChangeKeyDuplicate`は、通常の34アクション分の個別キー変更では`param_1`（action id）として3〜0x21の値しか使わない。`FUN_182296ef0`のcase 0/1（プリセット適用）が発火するのは`param_1`が0または1のときのみ。
- 前回・今回のdecompile読解では、`ChangeKey`自身の switch文は`param_1=0,1,2`を`default`扱いし、この経路には到達しないことを確認済み（`FUN_1822ee830(1)`を呼ぶだけで終わる）。
- したがって、**「誰が`FUN_182296ef0`をparam_1=0または1で呼び出すか」が依然UNRESOLVED**。`ChangeKey`/`ChangeKeyDuplicate`経由ではない、別の呼出元が存在するはずである。

`ChgConfigGamePad(uint Type, int value)`（Cpp2ILは本体が空/stubと報告していた）を疑い、metadata経由で真のVAを解決したところ`0x1810D6760`が得られた。これは他のdds3ConfigGamePadSteamメソッド群（`0x18228xxxx`〜`0x18229xxxx`帯）から大きく離れた`0x1810Dxxxxx`帯にある。raw disassemblyしたところ、通常のIL2CPPコード生成パターンではあった（`ConfigAllDateInit2`のような難読化ではない）が、内容はUnityの`GameObject`/`Component`探索らしき一連の呼出し（`0x182843250`, `0x18287f080`, `0x18283f980`等）が延々と続く、非常に大きな関数で、単純な「Type,valueを受け取ってconfig_data相当を1件更新する」という想定サイズを大きく超えていた。

```
UNRESOLVED:
ChgConfigGamePad(uint,int)の真の呼出先が
0x1810D6760で正しいのか（method index解決に
何らかの見落としがある可能性を排除できていない）、
正しいとして、なぜこれほど大きく
GameObject探索が主体の内容なのかは未確認。

この関数がFUN_182296ef0(param_1=0/1,...)を
内部で呼んでいるかどうかは、今回確認できていない
（関数が大きく、時間の都合で全体を追い切れなかった）。
```

**「FUN_182296ef0のcase 0/1を実際に誰がトリガーするか」は次回への最優先申し送り事項とする。**

### 13.4 更新後のEvidence一覧（§12.6への追加差分）

| 項目 | 前回評価 | 今回評価 |
|---|---|---|
| Cpp2ILの呼出先アドレス(0x1822B9E90)の信頼性 | FALSIFIED気味に報告 | **訂正: metadata ground truthで正しいと確認。Cpp2ILは正しかった** |
| `ConfigAllDateInit2`内部の難読化コード | 「実処理本体ではありえない」と断定 | **訂正: 実際にこのVAで正しく、難読化自体が実在する仕様。断定は撤回、原因はUNRESOLVED** |
| +0x48 field名 | UNRESOLVED | **CONFIRMED: "SelText"（metadata field順序で確認）。型の矛盾は依然UNRESOLVED** |
| Preset Apply Path（preset→config_data[4]） | UNRESOLVED（最大の空白） | **CONFIRMED: `FUN_182296ef0`のcase 0/1がその実体（config_data[4][0/1]=preset番号、かつ[3..33]をpresetからコピー）** |
| Runtime Apply/Sync Path（config_data[4]→Class B/GetConfigGamePad） | UNRESOLVED | **依然UNRESOLVED（Preset Apply Pathとは別物、混同しないこと）** |
| 上記トリガー元（誰が呼ぶか） | — | **新規UNRESOLVED（次回最優先）** |

### 13.5 次回への申し送り（優先順位付き）

1. **最優先**: `FUN_182296ef0`をparam_1=0または1で呼び出す呼出元の特定（`ChgConfigGamePad`の内部を最後まで読むか、他のxrefを探す）。
2. `ChgConfigGamePad`実VA(`0x1810D6760`)の解決結果自体の妥当性を、別の手法（例: Ghidraでこのアドレスにxrefがあるか、関数境界が自然か）で裏取りする。
3. dds3ConfigMainSteam+0x48の型をIl2CppType blobレベルで確定する（optional、config_data[4]周りの理解には必須ではなくなったため優先度を下げてよい）。
4. config_gamepad_priSetの中身（各presetの実データ）を静的抽出し、`config_data[4][3..33]`との対応を確認する。

## 14. 追補4（2026-09-22 続き4）— FUN_182296ef0正体判明 + caller全列挙

**用語整理を維持**: 本節は**Preset Apply Path**（`config_gamepad_priSet`→`config_data[4]`）のトリガー元調査であり、**Runtime Apply/Sync Path**（`config_data[4]`→Class B/`GetConfigGamePad`）とは別問題として扱う。後者は今回も未着手（UNRESOLVEDのまま）。**引き続きANALYSIS ONLY、native呼出・write・build・deploy・commit等は一切行っていない。**

### 14.1 重大な発見: FUN_182296ef0の正体（CONFIRMED）

§13で確立したmetadata直接解決ツールで`dds3ConfigGamePadSteam`の未解決メソッド群を機械的に解決したところ、

```
dds3ConfigGamePadSteam.cfgSetBit(UInt32 Type, ref Int32[] pFlag, Int32 value=0, Boolean execflag=True)
  -> VA = 0x182296EF0
```

**これが前回まで`FUN_182296ef0`と呼んでいた関数そのものである（CONFIRMED、VA完全一致）。** 4引数のC#シグネチャ（`Type, ref pFlag, value, execflag`）は、decompile済みの実引数（`param_1=uint, param_2=longlong*(≈ref Int32[]), param_3=int, param_4=char(≈bool)`）と完全に対応する。

**重要な意味の訂正**: `cfgSetBit`という名前が示す通り、この関数は**gamepad専用ではなく、Config画面全体で使われる汎用の「フラグ配列に値をセットする」ユーティリティ**である。対の`cfgGetBit`（VA `0x182296C90`、こちらも既に`ChangeKeyDuplicate`内で重複検出に使われているのを確認済み）と合わせて、Audio/Graphics/Display等の他タブでも同じ関数が再利用されている可能性がある（STRONGLY SUPPORTED、命名パターンから）。**「Type=0/1のとき何が起きるか」の意味（config_data[4]へのpreset一括適用）自体はCONFIRMED（§13.3）だが、その意味が発動するのはpFlag引数がconfig_data[4]を指している場合に限る**、という前提を明記する。

### 14.2 cfgSetBitの全caller（CONFIRMED、.textセクション全体をバイトスキャン）

capstoneではなく生バイトパターン（`E8`＝call opcode + rel32）で`.text`セクション全体（`0x19A000`〜`0x2BBC800`オフセット、約41MB）を走査し、`call 0x182296EF0`となる箇所を**網羅的に**列挙した（7件、これで全てであることをバイトスキャンの性質上保証できる）。各call siteを、§13.0で確立したmetadata法で得た全メソッドVAの並び順に照合し、所属メソッドを特定した:

| call site VA | 所属メソッド | 備考 |
|---|---|---|
| `0x18228DDB2` | `ArrowChangeMouse(Int32 _mEnter, Int32 _pos, Int32 _index)` | 新規判明 |
| `0x18228E27C` | `ChangeKeyDuplicate` | 既知（§1.3） |
| `0x18228E734` | `ChangeKey`（case 0x17） | 既知（§1.3） |
| `0x18228EDA5` | `GamePadDuplicateWrite()` | 新規判明（1件目） |
| `0x18228EDFA` | `GamePadDuplicateWrite()` | 新規判明（2件目、同一関数内で2回呼ぶ） |
| `0x18229231C` | `NoselChangeMouse(Int32 _mEnter, Int32 _pos)` | 新規判明 |
| `0x182292BDA` | `Updata()`（毎フレーム更新ループ） | 新規判明 |

**CONFIRMED: `cfgSetBit`の呼出元は上記7箇所で全て（.textセクション全体の直接call命令を網羅的にスキャン済み、間接呼出・仮想呼出は対象外）。**

### 14.3 各callerの引数解析（Type値の由来）

raw disassemblyで各call直前の引数セットアップを確認した:

- **`ArrowChangeMouse`**: `mov ecx, edi` — `edi`はレジスタ値（即値ではない）。関数シグネチャの第1引数`_mEnter`（真偽値、0か1しか取り得ない）が呼出規約上ediへ保存されたものと推定される（STRONGLY SUPPORTED、確定的な逆アセンブルではなくレジスタ用途の推定）。
- **`NoselChangeMouse`**: `mov ecx, esi` — 同様のパターン、`_mEnter`引数が由来と推定（STRONGLY SUPPORTED）。
- **`Updata`**: `mov ecx, ebx` — `ebx`はUpdata内部のローカル変数/ループ変数。`pFlag`（rdx）も`lea rdx, [rdi + rax*8]`という**動的に計算されたインデックス**であり、`ChangeKey`等が使う固定の`+0x40`（config_data[4]）パターンとは異なる（UNRESOLVED、複数tab/複数配列を横断的に扱うループの可能性が高い）。

```
CONFIRMED:
ArrowChangeMouse/NoselChangeMouseの第1引数は
"_mEnter"という真偽値的な名前を持ち、
0か1しか取らない可能性が高い
（ただしraw disassemblyだけで値域を証明したわけではなく、
 シグネチャの命名からの推定を含む＝STRONGLY SUPPORTED）。

UNRESOLVED:
ArrowChangeMouse/NoselChangeMouseが渡すpFlag
（rdx = baseObj + 0x40）のbaseObjが
本当にdds3GlobalWork.Instance.config_data
（＝GAMEPAD専用スロット）なのか、
それとも他tab用の汎用配列なのかは、
今回baseObjの生成元まで遡り切れておらず未確定。

UpdataのcfgSetBit呼出は、
動的index計算(rdi+rax*8)であることから、
複数の設定項目を横断的に処理するループの一部と推測されるが、
gamepad/config_data[4]が対象に含まれるかは未確認。
```

### 14.4 GamePadDuplicateWriteの役割（新知見）

`GamePadDuplicateWrite()`が`cfgSetBit`を2回呼んでいることがCONFIRMEDされた。これは`ChangeKeyDuplicate`が重複を検出した際に`g_chgkey`/`g_srcidx`/`g_dstidx`へ一時保存した状態（§1.3参照）を実際にcommitする関数であるという既存の推定（STRONGLY SUPPORTED）と整合する——2回の呼出は「新しい値をsrcへ」「元の値をdstへ」という交換(swap)処理に対応すると推測される（HYPOTHESIS、引数の詳細until追跡していない）。

### 14.5 startup X問題への示唆（まだ結論は出ない）

`ArrowChangeMouse`/`NoselChangeMouse`は共に**UI操作（マウスホバー）のコールバック**であり、**Config画面が実際に開かれて操作されない限り発火しない**。`ChgConfigGamePadAll`同様、「ゲーム起動直後（Config画面を一度も開いていない状態）」でこれらが実行されるとは考えにくい（STRONGLY SUPPORTED）。したがって、起動直後の`GetConfigGamePad(7)=X`という現象を「presetが自動適用されたから」と説明するのは、少なくともこの3つの新規callerからは支持されない。

```
現時点のstartup X問題への回答（結論保留、断定禁止の指示を遵守）:

HYPOTHESIS（変更なし）:
起動直後のX値は、Preset Apply Pathとは無関係に、
Class B側（GetConfigGamePadのread source）の
default/初期値がそのまま見えている可能性が高い。

UNRESOLVED:
Class B側の初期値がどこで設定されるか
（Init()、cctor、またはRuntime Apply/Sync Path未特定の関数）。
```

### 14.6 未実施（時間の都合で持ち越し）

- `ChgConfigGamePad(uint,int)`実VA(`0x1810D6760`)の別手法での再検証（今回未実施、前回の懸念のまま）。
- Runtime Apply/Sync Path（`config_data[4]`→Class B）のwriter探索（今回未着手、依然最大の空白）。
- `ArrowChangeMouse`/`NoselChangeMouse`のpFlag baseObj追跡完了。

### 14.7 更新後のEvidence一覧（§13.4への追加差分）

| 項目 | 評価 |
|---|---|
| `FUN_182296ef0`の正体 | **CONFIRMED: `dds3ConfigGamePadSteam.cfgSetBit`（汎用config-bit setter、gamepad専用ではない）** |
| cfgSetBitの全caller | **CONFIRMED（7件、.text全体バイトスキャンで網羅的に確認）** |
| ArrowChangeMouse/NoselChangeMouseがType=0/1を渡すか | STRONGLY SUPPORTED（`_mEnter`という真偽値パラメータ由来と推定） |
| これらがconfig_data[4]（GAMEPAD専用）を対象とするか | UNRESOLVED（pFlagのbaseObj未追跡） |
| Runtime Apply/Sync Path | 変更なし、UNRESOLVED |
| startup X問題 | 変更なし、HYPOTHESISのまま（断定せず） |

## 15. 追補5（2026-09-22 続き5）— Config Initial/Default Apply Pathの発見（Evidence表現訂正版）

**重要なEvidence訂正**: 当初「Runtime Apply/Sync Path（`config_data[4]`→runtime）のwriterを発見した」と報告したが、これは不正確だった。今回`ConfigAllDateInit2Tab`で実際にCONFIRMEDしたのは、

```
config_allInitdate由来のsource値
        ↓
ConfigAllDateInit2Tab(tabpos)
        ├→ config_data[tabpos][i]
        ├→ dds3ConfigMainSteam +0x48[0][tabpos][i]
        └→ dds3ConfigMainSteam +0x48[1][tabpos][i]
```

への**同時write**であり、「`config_data`からruntimeへのコピー/同期」ではなく、**「同一の初期値sourceから、config_dataとruntime/UI側の両方を同時初期化する経路」**である。これを**Config Initial/Default Apply Path**（または Common-source initialization path）と呼称し直す。**Preset Apply Path・Config Initial/Default Apply Path・Runtime Apply/Sync Pathの3つを明確に分離する。** 本来のRuntime Apply/Sync Path（`config_data`→runtimeの純粋なコピー/同期）の存在は依然UNRESOLVEDのままである。**引き続きANALYSIS ONLY、native呼出・write・build・deploy・commit等は一切行っていない。**

### 15.1 手法: dds3ConfigMainSteamの全72メソッドを自動スキャン

§13.0のmetadata直接解決ツールで`dds3ConfigMainSteam`の全72メソッドのVAを機械的に列挙し、各メソッド本体をcapstoneで自動disassembleして、(a)`+0xb8`（static_fields取得）から25命令以内に`+0x48`（またはその10進表記`72`）を参照する命令、(b)`[reg+reg*4+disp]`形式のInt32配列書込みらしき命令、の2パターンをヒューリスティックに検出した。72件中27件がヒットし、シグネチャ・命名から`ConfigAllDateInit2Tab(Int32 tabpos, ref Int32[][] config_data, Boolean backupinit=False)`（両条件を満たす）を最優先候補として精査した。

### 15.2 ConfigAllDateInit2Tab本体のraw disassembly（CONFIRMED、決定的）

`ConfigAllDateInit2Tab`（VA `0x1822B9770`）の全命令をbyte-exactに読んだ結果、以下が判明した:

```
引数: (Int32 tabpos=rbp相当, ref Int32[][] config_data=r13(ポインタのポインタ), Boolean backupinit=r12b)

r15 = rbp（＝tabposそのもの。変換・オフセット計算なし、直接代入）
rdi = *r13（config_data参照を1段deref、渡された配列オブジェクトを取得）

for (ebx = 0; ebx < (何らかの参照配列の長さ); ebx++) {
    // ebp==5のときのみ特殊分岐（詳細省略、config_allInitdate[5][ebx]がらみ）
    edi = config_allInitdate[5][ebx]  // または上記特殊分岐の結果

    // 1. 呼出元から渡されたconfig_data自体への書込み
    rax = *r13                         // config_data配列
    if (tabpos < rax.Length) {
        rcx = rax[r15]                 // = config_data[tabpos]      （r15=tabposを直接使用）
        if (ebx < rcx.Length) {
            rcx[ebx] = edi;            // ***WRITE*** config_data[tabpos][ebx] = edi
        }
    }

    if (backupinit) {
        // 2. dds3ConfigMainSteam自身のstatic +0x48構造への書込み（1つ目）
        rax = classAnchor.staticFields
        rcx = rax[+0x48]
        rax = rcx[0]                   // = (+0x48)[0]
        rcx = rax[r15]                 // = (+0x48)[0][tabpos]        ← tabpos=4ならGAMEPAD!
        if (ebx < rcx.Length) {
            rcx[ebx] = edi;            // ***WRITE*** (+0x48)[0][tabpos][ebx] = edi
        }

        // 3. dds3ConfigMainSteam自身のstatic +0x48構造への書込み（2つ目、別スロット）
        rax = classAnchor.staticFields
        rcx = rax[+0x48]
        rax = rcx[+0x28]               // = (+0x48)[1]  （0x28=0x20+1*8）
        rax = rax[r15]                 // = (+0x48)[1][tabpos]
        if (ebx < rax.Length) {
            rax[ebx] = edi;            // ***WRITE*** (+0x48)[1][tabpos][ebx] = edi
        }
    }
}
```

**CONFIRMED（byte-exact、決定的）: `ConfigAllDateInit2Tab(tabpos, ref config_data, backupinit)`は、`tabpos`をそのままインデックスとして使い、`config_data[tabpos][i]`と`dds3ConfigMainSteam.staticFields[+0x48][0][tabpos][i]`および`[+0x48][1][tabpos][i]`の3箇所へ同じ値を同時に書き込む。**

`GetConfigGamePad`が読む構造は`+0x48[0][4][innerIdx]`（§12.2でCONFIRMED済み、outer index固定4）。**したがって`tabpos=4`で`ConfigAllDateInit2Tab`が呼ばれれば、それは`GetConfigGamePad`が読む配列そのものへの書込みとなる。**

**これはConfig Initial/Default Apply Pathの実体である（CONFIRMED、`+0x48`側への書込み経路として初めて特定）。「config_data→runtimeのコピー/同期」ではなく「共通sourceからconfig_data/runtime双方への同時初期化」である点に注意。**

### 15.3 呼出元の確認: tabpos=4が実際に呼ばれている（CONFIRMED）

`ConfigAllDateInit2Tab`への全call siteを`.text`全体バイトスキャンで網羅的に検出したところ、**9箇所**が見つかった。全て`0x182706100`〜`0x182707070`という単一の狭い範囲（約0x1000バイト）に集中しており、**単一の呼出元関数（class/method名は今回未特定）が2回に分けて6タブ分(tabpos=0..5)を初期化していると推定される**。各call直前のtabpos引数セットアップをraw disassemblyで確認した結果:

| call site VA | tabpos引数 |
|---|---|
| `0x182706129` | レジスタ由来（おそらく0、1巡目の最初） |
| `0x182706177` | `lea ecx,[r9+1]` → **tabpos=1** |
| `0x1827061E5` | `lea ecx,[r9+4]` → **tabpos=4** ← GAMEPAD候補 |
| `0x182706237` | `lea ecx,[r9+5]` → **tabpos=5** |
| `0x182706862` | レジスタ由来（2巡目の最初） |
| `0x182706F59` | （即値0xE0、文脈未確認） |
| `0x182706FA7` | `lea ecx,[r9+1]` → tabpos=1 |
| `0x182707015` | `lea ecx,[r9+4]` → **tabpos=4** ← GAMEPAD候補（2巡目） |
| `0x182707067` | `lea ecx,[r9+5]` → tabpos=5 |

**CONFIRMED: `tabpos=4`で`ConfigAllDateInit2Tab`が呼ばれる箇所が実在する（2箇所、同一の呼出元関数内で2回）。**

```
CONFIRMED:
tabpos=4での呼出が実在する。

STRONGLY SUPPORTED:
tabpos=4がGAMEPAD tabに対応する
（GetConfigGamePadの固定outer index=4と一致するため）。
ただしtabpos番号とタブ種別の対応表そのものは
今回直接確認していない（config_allInitdate配列の
中身やTabStr配列の中身を読めば確定できるはずだが未実施）。

UNRESOLVED:
この呼出元関数（VA約0x182706000番台）が
どのクラス/メソッドか（今回未特定）。
呼ばれるタイミング（game boot / title init /
Controller Config画面open / save load後 等）も未確認。
```

### 15.4 更新後のデータフロー図（3経路を分離、訂正版）

```
[Preset Apply Path]
[config_gamepad_priSet[preset]]  (CONFIRMED実在)
        ↓ GetGamePadPriSet(preset) [CONFIRMED]
        ↓ cfgSetBit(Type=0/1, pFlag, preset, execflag=1) [CONFIRMED]
[dds3GlobalWork.Instance.config_data[4][3..33] = preset値]  (CONFIRMED)
※ このpFlagが本当にconfig_data[4]かは§14.3のとおり未確定（UNRESOLVED）


[Config Initial/Default Apply Path]  ← 今回新たにCONFIRMED
[dds3ConfigMainSteam.config_allInitdate[5][i]]  (存在はCONFIRMED、正体・意味はUNRESOLVED)
        ↓ ConfigAllDateInit2Tab(tabpos, ref config_data, backupinit) [CONFIRMED]
        ↓ 同一値を同時書込み（コピー/同期ではない）
   ├→ [config_data[tabpos][i]]                          (CONFIRMED)
   ├→ [dds3ConfigMainSteam+0x48[0][tabpos][i]]           (CONFIRMED)
   └→ [dds3ConfigMainSteam+0x48[1][tabpos][i]]           (CONFIRMED)
        ↓ tabpos=4の場合（STRONGLY SUPPORTED＝GAMEPAD）
[GetConfigGamePad(idx)が+0x48[0][4][idxごとのinnerIdx]を読む]  (CONFIRMED、§12.2)


[Runtime Apply/Sync Path]  ← 依然UNRESOLVED
「保存済みユーザー設定Y」がconfig_data/runtimeへどう反映されるかの経路は未発見のまま。
Config Initial/Default Apply Pathが「defaultを両方へ同時投入」するだけだとすれば、
ユーザー設定Yを反映する**別の経路**が存在するはずである（HYPOTHESIS、§15.5参照）。
```

**残る主要なUNRESOLVEDは複数ある**（1点ではない、訂正）: `ConfigAllDateInit2Tab(tabpos=4, ...)`の呼出元・呼出タイミング、`config_allInitdate`の正体（default値なのか、それ以外か）、そして保存済みユーザー設定Yが反映される別経路の有無。これらが判明すればstartup X→Y問題に直結する可能性が高い。

### 15.5 startup X→Y問題への仮説（HYPOTHESIS、断定しない）

```
Phase 1（HYPOTHESIS）:
ConfigAllDateInit2Tab(tabpos=4) が
config_allInitdate由来のdefault値Xを
config_data[4]とruntime(+0x48)の両方へ同時投入する。

Phase 2（HYPOTHESIS）:
その後、保存済みuser config Yがロードされる。

Phase 3（UNRESOLVED、経路未発見）:
Yが何らかの別Apply経路でruntimeへ反映される。
```

この3段階仮説は今回のConfigAllDateInit2Tabの発見と整合するが、**Phase 1が本当に「起動時」に実行されるか、Phase 3の経路が本当に存在するかは、いずれも未検証のままCONFIRMEDに格上げしない。**

## 16. 追補6（2026-09-22 続き6）— callerの正式特定 + 重大な反証（backupinit常にfalse）

**用語整理を維持**（Preset Apply Path / Config Initial-Default Apply Path / Runtime Apply-Sync Path の3分離）。**引き続きANALYSIS ONLY、native呼出・write・build・deploy・commit等は一切行っていない。**

### 16.1 手法: VA→managed method逆引きツールの構築

`Il2CppCodeGenModule.methodPointers`配列の全13083エントリを事前に(VA, local_idx)としてソートし、任意のVAに対して「直前に開始するメソッド」を二分探索で特定、そのメソッドが属す`型定義`を`typeDefinitions`テーブルの`methodStart`/`method_count`から逆引きする汎用ツールを構築した（Cpp2ILを一切経由しない、metadata+CodeGenModule直読み）。

### 16.2 ConfigAllDateInit2Tab(tabpos=4)の呼出元（CONFIRMED）

このツールで、§15.3で発見した9箇所のcall siteを含む関数群を特定した:

| VA範囲 | class | method |
|---|---|---|
| `0x1827060B0`〜 | `FsSaveData` | `SteamConfigLocalClear` |
| `0x182706250`〜 | `FsSaveData` | `SteamConfigLocalCopy` |
| `0x182706800`〜 | `FsSaveData` | `SteamConfigLocalCreate` |
| `0x182706AA0`〜 | `FsSaveData` | `SteamConfigLocalInit` |
| `0x182706BE0`〜 | `FsSaveData` | **`SteamConfigLocalLoad`** |
| `0x182707150`〜 | `FsSaveData` | `SteamConfigLocalSave` |

`FsSaveData`は名前の通りセーブデータ管理クラスであり、`SteamConfigLocalClear`/`Create`/`Init`/`Load`/`Save`/`Copy`という完全なライフサイクルを持つ。**`SteamConfigLocalLoad`がセーブファイルからconfig設定をロードする関数であることはCONFIRMED（クラス名・メソッド名から明白）。**

`ConfigAllDateInit2`（obfuscated wrapper）自身の3つの呼出元も同様に特定した:

| call site VA | class | method |
|---|---|---|
| `0x1824E015B` | `dds3GlobalWork_H.dds3GlobalWork_t` | **`Init`** |
| `0x1826010E3`, `0x1826010FF` | `SteamOptionFile` | **`LoadFile`** |

`dds3GlobalWork_t.Init()`（グローバルワーク自体の初期化）と`SteamOptionFile.LoadFile()`（オプションファイル＝セーブされた設定のロード）という、まさに「起動時初期化」と「保存済み設定ロード」に対応する2つの経路が、CONFIRMEDに特定できた。

### 16.3 重大な反証: 全ての既知callerでbackupinit=falseだった（FALSIFIED）

§15.2で確認した通り、`ConfigAllDateInit2Tab`の`dds3ConfigMainSteam+0x48`への書込みは`if (backupinit) {...}`でガードされている。今回特定した**全6箇所**のcall site（`SteamConfigLocalClear`×1、`SteamConfigLocalCreate`×1、`SteamConfigLocalLoad`×1、`dds3GlobalWork_t.Init`×1、`SteamOptionFile.LoadFile`×2）について、呼出直前のレジスタセットアップをraw disassemblyで確認したところ、**全てbackupinit引数に相当するレジスタ（`ConfigAllDateInit2Tab`ではr8、`ConfigAllDateInit2`ではrdx）へ`xor reg,reg`（＝0、false）をセットしていた。**

```
CONFIRMED（6/6箇所で再現、例外なし）:
SteamConfigLocalClear, SteamConfigLocalCreate, SteamConfigLocalLoad,
dds3GlobalWork_t.Init, SteamOptionFile.LoadFile(×2)
は全てbackupinit=falseでConfigAllDateInit2 / ConfigAllDateInit2Tabを呼ぶ。

FALSIFIED（前回§15.5のPhase1/2/3仮説の前提）:
「起動時にbackupinit=trueでdefault値が+0x48へ投入される」
という仮説は、今回調査した経路（game boot初期化・save load）
のいずれからも支持されなかった。
少なくともこれら6箇所の呼出だけでは、
dds3ConfigMainSteam+0x48（GetConfigGamePadのread source）は
一切更新されない。
```

**これは非常に重要な負の証拠である。** `config_data`（またはFsSaveDataのローカルバッファ）は`SteamConfigLocalLoad`/`dds3GlobalWork_t.Init`/`SteamOptionFile.LoadFile`を通じて更新されるが、**runtime側`+0x48`はこれらのどの経路からも更新されない。** つまり:

```
UNRESOLVED（新たな最重要課題）:
dds3ConfigMainSteam+0x48（[0]と[1]の両方）を
backupinit=trueで実際に更新する呼出元は、
今回発見した6箇所には存在しない。

別の呼出元が存在する（ConfigAllDateInit2Tab/ConfigAllDateInit2への
直接呼出がまだ他にもある可能性、または全く別の経路で
+0x48が書き込まれている可能性）か、
あるいは+0x48は起動後ずっとdefault値のままで、
GetConfigGamePadの「exploration後にYになる」現象は
+0x48以外の場所（例えば別のClass Bインスタンスや、
GetConfigGamePad自身が参照先を実行時に切り替えている等）
で説明される可能性も排除できない。
```

`ConfigAllDateInit`（tabなし版ラッパー、VA`0x1822BA0C0`）自体は今回もE8直接呼出が0件のままであり（§13.1）、Unity delegate/UnityAction/reflection経由で呼ばれている可能性が高い（STRONGLY SUPPORTED、直接呼出が見つからないことと、UI初期化コールバックとして典型的な登録パターンであることから）。**この経路こそがbackupinit=trueを渡す本命候補として残っている（UNRESOLVED、次回最優先）。**

### 16.4 startup X→Y問題への示唆（仮説を訂正）

```
訂正版HYPOTHESIS:

+0x48（runtime、GetConfigGamePadのread source）は、
SteamConfigLocalLoad/dds3GlobalWork_t.Init/SteamOptionFile.LoadFile
のいずれからも更新されない。

したがって「起動直後のX」も「探索後のY」も、
これら6経路とは別の場所で+0x48に書き込まれている
はずである。

ConfigAllDateInit（delegate/reflection経由と推定）が
その候補として最有力だが、未確認。
```

**この訂正により、前回のPhase1/2/3仮説は撤回する。** 「defaultが先に入り、userの値が後から来るが+0x48だけ更新されない」という単純な話ではなく、**+0x48の更新経路そのものが依然完全に未発見**という、より根本的な空白が残っている。

## 17. 追補7（2026-09-23）— +0x48 writer全列挙、ConfigAllDateInit間接参照、GetConfigGamePad(7)経路のbyte-exact確定

前回セッションからの再開。開始前に`GameAssembly.dll`のSHA-256を再計算し、`59adbb5b18aaedc7df6c1672790ca48be99b5e8559c81db27132c436fb0a9fc4`（canonical、大文字小文字を除き一致）を再確認した（CONFIRMED、差分なし）。**引き続きANALYSIS ONLY。native呼出、ゲームメモリ書込み、hook、build、deploy、commit、push、branch切替、stash pop は一切行っていない。**

### 17.0 手法（新規ツール、Pythonスクリプト。scratchpad内に作成、リポジトリ外）

`.analysis/resolve_name_to_va.py` / `resolve_va_to_name.py` / `resolve_nearest_method.py` のロジックを流用し、以下を新規実装した（全てread-only、`GameAssembly.dll`と`global-metadata.dat`のバイト列読み込み＋pefile/capstoneによる静的disassembleのみ）:

1. `meta_common.py` — 上記3ツールを1モジュールに統合し、`nearest_method_below(va)`（二分探索によるVA→managed method逆引き）を追加。**サニティチェック: 既知7件のCONFIRMED済みVA（ChangeKey/ChgConfigGamePadAll/ChangeKeyDuplicate/ConfigAllDateInit/ConfigAllDateInit2/ConfigAllDateInit2Tab/cfgSetBit）全てで完全一致を確認済み（7/7 OK）。**
2. `scan_anchor_xrefs.py` — `dds3ConfigMainSteam`のIl2CppClass*グローバルキャッシュセル`0x182E4EBA0`（§12.3でCONFIRMED済み）へのRIP相対参照を`.text`セクション全体（約41MB、803万命令）から機械的に列挙。
3. `trace_writers.py` — レジスタteint（taint）モデルによる`classptr→+0xB8(static_fields)→+0x48(SelText)→さらに深い添字`の到達chainを関数単位で自動追跡し、STORE命令（`mov`系および`add/sub/and/or/xor/inc/dec`等のRMW系）に到達するものを候補WRITEとして検出する。単純な「+0x48という文字列がテキストに現れるか」ではなく、**大元のクラスアンカー由来のレジスタteintを追跡することで、無関係な別クラスの同一offset(+0x48)フィールドとの混同を排除している**（後述17.1参照、初期実装の`test`命令誤判定バグは発見・修正済み）。
4. `scan_code_addr_refs.py` / `find_callers.py` — `call`/`jmp`（E8/E9直接分岐）に加え、RIP相対`lea`（アドレス取得）・即値`mov reg,imm64`によるコードアドレス参照も対象に含めた間接参照探索（前回セッションの`find_native_xrefs.py`はE8/E9の直接分岐のみが対象だった点を拡張）。

**重要な注意（指示どおり）**: 本節の「writer候補」「到達chain」は全て**単一パスの線形disassembly＋レジスタteintのみ**による検出であり、分岐の実行可能性（到達性）やループの反復回数を実行時意味論として検証したものではない。個別にraw disassemblyで手動確認した箇所（後述）のみCONFIRMEDとし、それ以外はSTRONGLY SUPPORTED/HYPOTHESISに留める。**単純な静的パターン検索だけでは完全網羅と断定しない**（cfgSetBit系のような、ポインタを引数で渡して呼び出し先の中で実際のSTOREが行われるパターンは、関数単位のteint追跡では原理的に検出できない）。

### 17.1 Task 1: `dds3ConfigMainSteam+0x48`配下へのwriter全列挙

**手順と件数（CONFIRMED、機械的列挙）**:
```
0x182E4EBA0への直接RIP相対参照: 824箇所（.text全体を803万命令disassembleして網羅的に検出）
  ↓ 所属managed methodへ集約
一意な関数: 169個
  ↓ 各関数内でteint追跡（classptr→+0xB8→+0x48）
+0x48へ実際に到達する関数: 20個
  ↓ そのうちSTORE命令（=書込み）に到達する関数
候補WRITE関数: 6個
```

**候補WRITE 6件の内訳（各VA、write命令、対象、実行条件、Evidence）**:

| # | VA (managed method) | write命令 | 書込み対象 | 実行条件 | Evidence |
|---|---|---|---|---|---|
| 1 | `0x1822B9770` `dds3ConfigMainSteam.ConfigAllDateInit2Tab` | `mov [rdi+rbp*8+0x20], edi`等（ループ内、既知） | `+0x48[0][tabpos][i]` **かつ** `+0x48[1][tabpos][i]` 両方 | `if (backupinit)` ガード内（§15.2既知） | CONFIRMED（既存、raw disassemblyで再確認、変化なし） |
| 2 | `0x1822CDC10` `dds3ConfigMain..cctor` | `0x1822CE303: mov qword ptr [rdx+0x48], rax` | `+0x48`フィールド自体（新規allocした空配列オブジェクトの代入） | 無条件（cctor内、分岐なしの直線コード） | **CONFIRMED（新規発見）: `dds3ConfigMain`の静的コンストラクタが、`dds3ConfigMainSteam.SelText`フィールドに新規allocした空配列を代入している。** ただし直後に個別要素への即値store命令は存在しない（allocと同時に空のまま代入、CONFIRMED — 割込みなく次のフィールド`+0x50`のalloc処理に進むため、リテラル値は入っていない） |
| 3 | `0x1822CF850` `dds3ConfigMain.dds3ConfigProcessStart` | `0x1822CFA15: mov qword ptr [rdi+rbp*8+0x20], rsi` | `+0x48[1][tabpos]`（`tabpos=0..5`、6タブ全件をループ） | ループ条件`ebx<6`のみ、backupinit相当のガードなし | **CONFIRMED（新規発見）: `rsi = dds3GlobalWork.Instance.config_data[tabpos]`を`SelText[1][tabpos]`へ書き込むループ。Config画面を開く処理(`dds3ConfigProcessStart`)の中で、config_data→SelText[1]の直接コピーが実行される。** ただし対象は`[1]`スロットであり、`GetConfigGamePad`が読む`[0]`スロットではない（§12.2でCONFIRMED済みの読み出し経路とは別）。 |
| 4 | `0x1827D9140` `dds3ConfigDisplaySteam.SetConfigDisplay` | `mov dword ptr [rcx+0x28], ebx` 等（2箇所） | `+0x48[0][3][idx]` **かつ** `+0x48[1][3][idx]` 両方 | 無条件（関数に到達すれば直線的に両方書く） | **CONFIRMED（新規発見）** |
| 5 | `0x1827D9650` `dds3ConfigDisplaySteam.SetConfigResolution` | 同上パターン | `+0x48[0][3][idx]` **かつ** `+0x48[1][3][idx]` | 同上 | **CONFIRMED（新規発見）** |
| 6 | `0x1827D99C0` `dds3ConfigDisplaySteam.SetConfigScreenMode` | 同上パターン | `+0x48[0][3][idx]` **かつ** `+0x48[1][3][idx]` | 同上 | **CONFIRMED（新規発見）** |

**重要な意味づけ**:
- \#4〜6は**tabpos=3が"DISPLAY"タブであることの新規CONFIRMED証拠**でもある（`dds3ConfigDisplaySteam`クラスが`+0x48[*][3]`を直接書いている）。これは§15.3で「tabpos番号とタブ種別の対応表は未確認」としていた点の部分的解決である。
- \#4〜6は、**「UIで設定を変更した瞬間に`+0x48[0][tabpos]`（`GetConfigGamePad`等が読む側＝スロット`[0]`）を直接書き換える」という、まさに探していた"Runtime Apply/Sync Path"そのものの実例**である（DISPLAYタブに限り発見）。
- **しかし、GAMEPADタブ（tabpos=4）に対応する同種の「SetConfigGamePad」的な関数は存在しない**（`dds3ConfigGamePadSteam`の全42メソッドをmetadataから機械的に列挙し確認、CONFIRMED——`ChgConfigGamePad`/`ChangeKey`/`cfgSetBit`等はいずれも`config_data`側のみを操作し、`dds3ConfigMainSteam`のクラスキャッシュセル自体を一切参照しない。824件の網羅的xrefスキャンに`ChgConfigGamePad`(VA `0x1810D6760`)が1件も現れないことがその直接証拠、CONFIRMED）。

**field48到達20関数のうち残り14件（読み出しのみ、write未検出）**: `GetConfigGamePad`, `GetConfigGamePadGuideKID`, `GetConfigKeyBoard`, `GetConfigKeyBoardGuideKID`, `GetKeyBoardPadMap`, `DatePrev`, `DateSave`, `GetConfigBright`, `GetConfigContrast`, `cfgChkFlagChange`, `cfgCopyFlagBakToGbl`, `cfgCopyFlagGblToBakTab`, `dds3DestroyConfig`, `FsSaveData.SteamConfigLocalCopy`。teint追跡上は読み出し（`nested_load`/`nested_compare`）のみで、書込み命令には到達しなかった（STRONGLY SUPPORTED、上記手法の限界内で）。

### 17.2 Task 2: `ConfigAllDateInit`間接参照の探索

**新規発見（jmp/tail-callを含めた拡張スキャンで）**:

```
ConfigAllDateInit2 (VA 0x1822B9E90) への call/jmp 全箇所（direct E8/E9のみ、LEA/imm64は0件）:
  既知3件（§16.2）: dds3GlobalWork_t.Init, SteamOptionFile.LoadFile ×2  （backupinit=false, 既知）
  新規3件:
    0x1822BA159 jmp -> ConfigAllDateInit自身の内部（VA 0x1822BA0C0、末尾tail-call）
    0x1822C8880 jmp -> dds3ConfigMainSteam.cfginitialize(bool)の内部（true分岐末尾）
    0x1822CF570 jmp -> dds3ConfigMain.cfginitialize(bool)の内部（true分岐末尾）
```

**前回セッション（§16.2）がこの3件を見落としていた原因（CONFIRMED）**: 前回の`find_native_xrefs.py`はE8（call）のみを対象としており、**E9（jmp、末尾呼び出し/tail-call）を検索対象に含めていなかった**。今回`scan_code_addr_refs.py`でE9も含めて再スキャンし、上記3件を新規発見した。

**新規2件（cfginitialize系）のraw disassembly確認（CONFIRMED、byte-exact）**:
```
dds3ConfigMainSteam.cfginitialize(bool flag) [VA 0x1822C87A0]:
  if (!flag) goto 別分岐（ConfigAllDateInit2を一切呼ばない、Graphics系cfgSetBitを呼ぶだけ）
  // flag!=0の場合:
  rbx = &dds3GlobalWork.Instance.config_data   (+0xE8への参照計算)
  r8d = 0    // 末尾のMethodInfo*相当（NULL、非ジェネリック呼出）
  edx = 0    // ★backupinit = 0 (FALSE)★
  rcx = rbx
  jmp ConfigAllDateInit2                        // tail-call, backupinit=false

dds3ConfigMain.cfginitialize(bool flag) [VA 0x1822CF450]:
  （↑と完全に同一の構造、同じくbackupinit=0固定）
```

**結論: 新規発見の2箇所も含め、`ConfigAllDateInit2`/`ConfigAllDateInit2Tab`への静的に発見できた全call/jmp箇所（8箇所、既知6＋新規2）は例外なくbackupinit=falseである（CONFIRMED、6/6→8/8に更新、FALSIFIED仮説は変わらず維持）。**

**`ConfigAllDateInit`自体（VA `0x1822BA0C0`、C#シグネチャの既定値`backupinit=True`）への参照は、tail-call(0x1822BA159, これは自分自身のjmpなので"呼出元"ではなく"自分の中身")を唯一の例外として、それ以外は`.text`全体（call/jmp/RIP相対lea/即値mov reg,imm64の4パターン全て）を通じて実質ゼロ件（CONFIRMED、網羅的スキャン）。バイナリ全体を通じても`0x1822BA0C0`という8byte値が出現するのは`.debug`セクション内の1箇所（reflection/デバッグ用metadataテーブルとみられ、実行可能コードからの参照ではない）のみである（CONFIRMED）。**

```
STRONGLY SUPPORTED:
ConfigAllDateInit()（backupinit=True既定）は、
本ビルドにおいて静的に発見可能な呼出元を一切持たない。
リフレクション/デリゲート経由（Type.GetMethod+Delegate.CreateDelegate、
Unity SendMessage的な文字列dispatch等）で呼ばれているか、
あるいは単に到達不能な死んだコードである可能性が高い。
いずれかは静的解析のみでは切り分け不能（UNRESOLVED、実機実験が必要）。
```

**唯一発見できた"backupinit=false系"呼出の実際の呼出元（起動シーケンス上の位置づけ、CONFIRMED）**:
```
dds3TitleMain.dds3Titlemain（タイトル画面のstate machine、jump table dispatch、case=0x2b）
  ecx=0, edx=0 で dds3ConfigMain.cfginitialize(false) を呼ぶ
  → flag=falseなので上記のConfigAllDateInit2分岐には到達しない
  → 代わりに dds3ConfigGraphicsSteam.cfgSetBit(Type=5, ...) を呼ぶだけ（+0x48/SelTextと無関係）
```
**つまりタイトル画面到達時点で確認できる唯一の`cfginitialize`呼出は、`+0x48`に一切到達しない（CONFIRMED）。**

`dds3ConfigMainSteam.cfginitialize`（もう一方）は、call/jmp/lea/imm64/絶対8byte値のいずれの形でも静的な呼出元が1件も見つからなかった（`.debug`セクション内のreflection metadataエントリ1件を除く。CONFIRMED、網羅的スキャン）。命名の対称性（`dds3ConfigMain.cfginitialize`と`dds3ConfigMainSteam.cfginitialize`）から、共通のインターフェースを介した仮想呼出（vtable経由、直接アドレス参照を残さない）で呼ばれている可能性が残る（HYPOTHESIS、未検証）。実機の観察事実（「探索状態(exploration)開始後にY値へ変化する」）と、`dds3TitleMain`と対になる「Field/Game main」的なstate machineクラスが存在するはずだという構造的な類推から、**もしこの`dds3ConfigMainSteam.cfginitialize(true)`版がそうしたクラスから呼ばれていれば、まさに探しているbackupinit=true経路の最有力候補となる**（HYPOTHESIS、次回最優先）。

### 17.3 Task 3: `GetConfigGamePad(7)`経路のbyte-exact確定

§12.2で「idx=7 jump target: `0x182290110`（推定、未確認）」とされていた値を、jump table（RVA `0x2291300`）の該当エントリを直接バイト読みして訂正した:

```
CONFIRMED（訂正）: idx=7の実際のjump target = 0x18229011A
（従来の推定値0x182290110とは0xA différent。今回初めてraw disassemblyで確認）
```

**idx=7のraw disassembly（CONFIRMED、byte-exact、idx=4/5と同一パターン）**:
```
classPtr = [キャッシュセル 0x182E4EBA0 経由、init-checkあり]
staticFields = [classPtr + 0xB8]
arr0 = [staticFields + 0x48]        ; null/length(>0)チェック
arr1 = [arr0 + 0x20]                ; = arr0[0]、null/length(>4)チェック
arr2 = [arr1 + 0x40]                ; = arr1[4]、null/length(>0xA=10)チェック
return *(int*)(arr2 + 0x48)         ; = arr2[10]  ← 4byteストライド
```

**ついでにidx=6, 8, 9も同一手法でbyte-exact確認し、完全な対応表を得た（CONFIRMED）**:

| idx | jump target VA | inner index (arr2[N]) |
|---|---|---|
| 4 | `0x18228FFA3` | 7 (既知) |
| 5 | `0x182290020` | 8 (既知) |
| 6 | `0x18229009D` | 9 (新規byte-exact確認) |
| 7 | `0x18229011A` | **10（新規byte-exact確認、従来"推定"だった値を訂正）** |
| 8 | `0x182290197` | 11 (新規byte-exact確認) |
| 9 | `0x182290214` | 12 (新規byte-exact確認) |

パターンは`innerIdx = idx + 3`（idx=4..9の範囲でCONFIRMED、idx=10以降は未確認）。

**startup X→Y問題への到達点（結論、断定しない）**:

実機事実の「起動初期 `GetConfigGamePad(7)`=X」は、これで曖昧さなく「`SelText[0][4][10]`の値」であると確定した（CONFIRMED、経路の疑義は解消）。

しかし、Task1（writer全列挙）・Task2（間接参照探索）を尽くしても、**`SelText[0][4]`（スロット`[0]`、`GetConfigGamePad`が読む側）へ実際にraw12/raw9/raw11等のリテラル値を書き込む静的経路は依然として1件も発見できていない**:
- `dds3ConfigMain..cctor`は空配列を`allocate`して代入するのみ（値は入れない、CONFIRMED）。
- `ConfigAllDateInit2Tab`のtabpos=4書込みは、発見できた全8箇所の呼出元で例外なくbackupinit=falseであり、`[0]`/`[1]`いずれへも到達しない（CONFIRMED×8）。
- `dds3ConfigProcessStart`は`[1]`のみを書く（`[0]`ではない、CONFIRMED）。
- DISPLAYタブに存在する「SetConfigX」直接書込みパターンの、GAMEPADタブに対応する版は存在しない（CONFIRMED、`dds3ConfigGamePadSteam`の全メソッド精査済み）。

```
UNRESOLVED（Task1/2を尽くした上での結論）:
SelText[0][4]（GetConfigGamePadの読み出し元）へ
実際の数値を書き込む経路は、静的解析で発見できた
範囲には存在しない。

残る2つの排他的でない仮説（いずれも未検証）:
(a) dds3ConfigMain..cctorの中で、SelTextへの空配列代入(+0x48)
    直後に割り当てられている他フィールド(+0x50, +0x58等)が
    実は本当のgamepadデフォルト値の格納先であり、
    §12.2以来の「+0x48 = GAMEPADの読み出し元」という
    field特定そのものを再検証する必要がある可能性
    （cctor自体はSelTextに値を書いていないことをCONFIRMED
    したのみで、+0x48の意味自体を疑うには至っていない —
    idx=4/5/7のraw disassemblyでarr0=[+0x48]の3階層降下が
    byte-exactに再現しているため、+0x48がGetConfigGamePadの
    read sourceであること自体はCONFIRMEDのまま揺るがない。
    従って(a)は「cctorが他に何かを見落としている」という
    弱いHYPOTHESISに留まる）。
(b) dds3ConfigMainSteam.cfginitialize(true)、または
    ConfigAllDateInit()自身が、Title画面とは別の
    起動/探索開始シーケンス（未特定のField/Game main
    state machine）から静的に追跡不能な形
    （リフレクション、vtable仮想呼出、delegate等）で
    呼ばれている可能性（HYPOTHESIS、次回最優先）。
```

### 17.4 更新後のEvidence一覧（§16.4への追加差分）

| 項目 | 前回評価 | 今回評価 |
|---|---|---|
| `+0x48`writerの全列挙 | 未実施（Task1着手前） | **機械的網羅スキャン実施済み：824 xref→169関数→20到達→6 write候補（詳細§17.1）。ただし単一パス静的teintの限界内であり完全網羅とは断定しない** |
| `ConfigAllDateInit2`への呼出元 | 6箇所（backupinit=false全件） | **8箇所に更新（backupinit=false全件、変わらず）。E9(jmp)を見落としていた前回手法のgapをCONFIRMED** |
| `ConfigAllDateInit`自体の呼出元 | 直接E8呼出0件（未確認、STRONGLY SUPPORTED止まり） | **CONFIRMED: call/jmp/lea/imm64/絶対ポインタの4パターン全てで実質0件（自分自身のtail-callを除く）。リフレクション/デッドコード疑惑がSTRONGLY SUPPORTEDに格上げ** |
| `dds3ConfigMainSteam.cfginitialize`の呼出元 | 存在未知（前回未発見の関数） | **新規発見も呼出元0件（CONFIRMED、次回最優先の空白）** |
| `dds3ConfigMain.cfginitialize`の呼出元 | 同上 | **新規発見。`dds3TitleMain.dds3Titlemain`から`flag=false`で呼ばれることをCONFIRMED（ただし+0x48には無関係な分岐に入る）** |
| GetConfigGamePad idx=7 jump target | `0x182290110`（推定、未確認） | **CONFIRMED訂正: `0x18229011A`（byte-exact）、inner index=10** |
| tabpos=3のタブ種別 | UNRESOLVED | **CONFIRMED: DISPLAY（`dds3ConfigDisplaySteam`が`+0x48[*][3]`を直接書込むことから）** |
| SelText[0][4]（GAMEPAD、GetConfigGamePad読み出し元）への数値write経路 | UNRESOLVED（最大の空白） | **依然UNRESOLVED。ただしTask1/2を尽くした結果として「発見できなかった」という否定的証拠の強度が大幅に向上（cctor/ConfigAllDateInit2Tab/dds3ConfigProcessStart/Display系の5経路全てで個別にCONFIRMEDな除外が取れた）** |

### 17.5 次回への申し送り（優先順位付き）

1. **最優先**: `dds3ConfigMainSteam.cfginitialize`の呼出元特定。静的なcall/jmp/lea/imm64が0件だったため、(a) vtable/インターフェース経由の仮想呼出である可能性を、当該インターフェースのvtable配置をmetadataから機械的に洗い出して検証する、(b) それでも見つからなければ、read-only実機実験（後述）に切り替える。
2. `dds3ConfigMain..cctor`の完全な行単位トレース（今回は`+0x48`周辺のみ）。`+0x50`/`+0x58`/`+0x60`/`+0x64`フィールドが何であるか（config_allInitdateやconfig_gamepad_priSet等の既知フィールドとの対応）を特定すれば、cctorが本当に「空配列のみ」で数値を持たないことの追加裏付けになる。
3. `dds3TitleMain.dds3Titlemain`の代わりに存在するはずの「Field/Game main」state machineクラスの特定（実機観察の「探索開始後にYへ変化」に対応する起動フェーズを担うクラス）。dds3TitleMainと同様のjump-table dispatch構造を持つクラスを`.text`から機械的に検索することで候補を絞れる可能性がある。
4. `ChgConfigGamePad`(VA `0x1810D6760`)の内部を最後まで読む（§13.3から持ち越し、今回も未着手）。dds3ConfigMainSteamを参照しないことは今回CONFIRMEDだが、config_data側で何をしているかは依然UNRESOLVED。

**静的解析が行き詰まった場合の最小read-only実機実験案（次回、静的解析で(1)が解決しなければ検討）**:
- Ghidraのバイト値検索ではなく、実機で「タイトル画面を経由せずセーブデータをロードして直接フィールドへ入る」デバッグ手段があれば、`dds3TitleMain`実行の有無とX→Y遷移の有無の相関を見ることで、cfginitializeのtrue分岐がタイトル画面由来かフィールド遷移由来かを切り分けられる可能性がある（ただし現状の権限ではnative実行やメモリ改変は行わない前提のため、既存のゲームプレイ操作の範囲内でのタイミング観察に限定する）。

## 18. 追補8（2026-09-23 続き）— SelText[0]/[1]/config_dataのalias解析、Runtime Apply/Sync Pathの実体発見

前回（§17）からの継続セッション。開始前にHEAD/detached/git status/stashを再確認し、§1〜17の既存差分および`SESSION_RESUME_NOTES.md`の差分に変化がないことを確認した（CONFIRMED、不一致なし）。**引き続きANALYSIS ONLY。native実行・hook・ゲームへのwrite・build・deploy・commit・pushは一切行っていない。**

前回セッションのscratchpad（`meta_common.py`, `anchor_xrefs.txt`, `trace_writers_report.txt`等）はセッション間で保持されており再利用した。

### 18.0 今回の出発点（前回§17からの引継ぎ）

前回、`SelText[0][4]`（`GetConfigGamePad`の読み出し元）へ実際の数値を書き込む経路が、`+0x48`への到達を機械的に検出する範囲では1件も見つからなかった。今回はユーザー指示どおり「個別Int32 storeではなく、配列参照そのものの代入・別名化(alias)」という角度から再調査した。

### 18.1 Task 1: SelText[0][4]・SelText[1][4]・config_data[4]の参照関係

**構造の再確認（CONFIRMED、byte-exact）**:
```
dds3ConfigMainSteam.SelText（native offset +0x48）
  = Int32[][][] 相当の3階層構造（§12.2/17.3で複数idxにより確認済み、変化なし）
  arr0 = SelText                       (トップレベル、+0x48自体)
  arr0[0] = SelText[0]  (= *(arr0+0x20))   ← GetConfigGamePad等が読む"live"スロット
  arr0[1] = SelText[1]  (= *(arr0+0x28))   ← "編集用バッファ"スロット（後述で機能確認）
  arr0[X][tabpos] = SelText[X][tabpos] (= *(arr0[X] + tabpos*8 + 0x20))  ← 各タブ用Int32[]

dds3GlobalWork.Instance.config_data（native offset +0xE8、Instance経由の別クラス）
  config_data[tabpos] = *(config_data + tabpos*8 + 0x20)   ← セーブ/編集側の各タブ用Int32[]
```

**alias/copy関係（新規発見、CONFIRMED）**: 前回§17.1で発見した`dds3ConfigProcessStart`の書込み(`0x1822CFA15: mov qword ptr[rdi+rbp*8+0x20], rsi`)を再度byte-exactに確認したところ、これは**要素コピーではなく参照代入（shallow alias）である**ことが確定した:
```
rsi = config_data[tabpos]          ; 直前に mov rsi,[rax+rbp*8+0x20] でロード（rax=config_data配列）
rdi = SelText[1]                    ; arr0[1]
[rdi + rbp*8 + 0x20] = rsi           ; SelText[1][tabpos] = config_data[tabpos]（"="は参照代入、要素コピーではない）
```
このstore命令の直前に個別要素へのコピーループは存在せず、単純に**config_data[tabpos]というInt32[]オブジェクトへの参照をそのままSelText[1][tabpos]スロットへ書き込んでいる**（scale=8/QWORDストライドのSIB addressing、CONFIRMED——これはInt32配列の「配列そのもの」を指すポインタサイズであり、Int32要素(4byte, scale=4)のコピーではないことがオペランドのstride幅から機械的に判別できる）。

**結論**: `dds3ConfigProcessStart`実行直後、`SelText[1][tabpos]`と`config_data[tabpos]`は**同一オブジェクトを指す（shallow alias、CONFIRMED）**。以後、`config_data[tabpos][i]`へ書き込むどんなコード（`cfgSetBit`/`ChangeKey`経由等）も、**追加の明示的write命令なしに`SelText[1][tabpos][i]`からも同じ値が見える**ことになる。ただし`SelText[0]`（`GetConfigGamePad`の読み出し元）はこの時点では別オブジェクトのままであり、このaliasだけでは`[0]`側は更新されない（§18.2で決着）。

`ConfigAllDateInit2Tab`のbackupinit=true分岐（前回§17.1で確認済み）は、`SelText[0][tabpos]`・`SelText[1][tabpos]`の両方に対して**都度新規allocateした別々の配列**を代入する（`call 0x1800e65b0`が2回、別々の戻り値`rsi`を使用、CONFIRMED）。したがってこの経路が実行されても`[0]`と`[1]`、あるいは`config_data`との間でaliasは発生しない（back-upinit=trueで実行されればゼロから独立した配列に置き換わるのみ）。

### 18.2 Task 2: 既存Copy/Prev/Save関数のコピー方向（最重要の発見）

`DatePrev`/`DateSave`が共通して呼ぶネイティブ関数`0x1813F8560`を今回初めてraw disassemblyした。**これはAssembly-CSharp範囲外（VA `0x1813Fxxxxx`帯、BCL/mscorlib相当のIL2CPPコード生成領域）にあり、内部で型サイズ取得(`0x180089ec0`)→要素数/範囲チェック(`0x18008b4c0`等)→本体`0x1813F8650`（`Array.Copy`の下請け実装と酷似した引数構成: source, sourceIndex, dest, destIndex, length）を呼ぶ構造になっている。**

```
CONFIRMED: 0x1813F8560(rcx, rdx, r8d, r9d) は
  Array.Copy(source=rcx, destination=rdx, length=r8d) 相当のBCL配列コピー関数である
  （型チェック・範囲チェックを経て内部copy処理へ委譲する構造から判断。
   単純な比較(Equals)関数ではなく、副作用としてdestinationへ要素コピーを行う）。
```

この事実を踏まえて`DatePrev`/`DateSave`の呼出しをレジスタ割当まで含めてbyte-exactに再確認した:

**`DatePrev(Int32 tabpos)`（VA `0x1822BA6D0`）**:
```
rbp = SelText[0][tabpos]
rsi = SelText[1][tabpos]
r8d = config_data[tabpos].Length
rdx = rsi (=SelText[1][tabpos])
rcx = rbp (=SelText[0][tabpos])
call 0x1813F8560   →  Array.Copy(source=SelText[0][tabpos], destination=SelText[1][tabpos], length=config_data[tabpos].Length)
```
**CONFIRMED: `DatePrev`は `SelText[1][tabpos] = SelText[0][tabpos]`（"live"→"編集バッファ"へのコピー、[0]→[1]方向）を行う。**

その直後、tabpos別のjump table（RVA `0x22ba974`、byte-exactに全6件decode済み）で分岐し、**tabpos=4のケースは`dds3ConfigGamePadSteam.ChgConfigGamePadAll()`（VA `0x18228E9B0`）を無条件に呼ぶ**（比較結果のbool戻り値は一切参照されず、常に実行される、CONFIRMED）。`DatePrev`は`dds3ConfigMainSteam.EndChoiceUpdate`（VA `0x1822BB430`）からのみ呼ばれ（4箇所、CONFIRMED、`.text`全体の網羅的E8/E9スキャン）、`EndChoiceUpdate`自体は`dds3UpdateConfig`（Config画面の毎フレーム更新ループ）からのみ呼ばれる（CONFIRMED）——**Config画面が開いている間のUI操作（タブ確定操作）でのみ到達する。**

**`DateSave()`（VA `0x1822BA990`）**:
```
if (cfgChkFlagChange() != 1) return;   // 変更が無ければ何もしない

// ループ1（tabpos=0..5）:
rcx = SelText[1][tabpos]
rdx = config_data[tabpos]
call 0x1813F8560   →  Array.Copy(source=SelText[1][tabpos], destination=config_data[tabpos], length=config_data[tabpos].Length)

// ループ2（tabpos=0..5、独立した別ループ）:
rcx = SelText[1][tabpos]
rdx = SelText[0][tabpos]
call 0x1813F8560   →  Array.Copy(source=SelText[1][tabpos], destination=SelText[0][tabpos], length=SelText[1][tabpos].Length)

（その後 tabpos別に 0x182706000 系の呼出し＝FsSaveData関連の保存処理、
 続いてtab種別チェックとdds3ConfigDisplaySteam関連呼出し、
 最後に [classPtr+0x1d9] へ 1 を書込むフラグ設定）
```
**CONFIRMED（決定的）: `DateSave()`のループ2は `SelText[0][tabpos] = SelText[1][tabpos]` を全6タブについて実行する。これは`tabpos=4`（GAMEPAD）を含む。すなわち`GetConfigGamePad`が読む`SelText[0][4]`を実際に更新する経路が、これで初めて発見された。**

`DateSave`は`dds3ConfigMainSteam.EndChoiceUpdate`（1箇所）と`dds3ConfigMainSteam.dds3UpdateConfig`本体（1箇所、`cfgChkFlagChange()!=0`かつ現在タブが2/3以外という条件付きでunconditionalに呼ばれる分岐）の2箇所から呼ばれる（CONFIRMED、網羅的E8/E9スキャン）。**両方とも`dds3UpdateConfig`（Config画面の毎フレーム更新ループ）の実行を前提とする——Config画面が開いていない状態では到達しない。**

**`cfgChkFlagChange()`（VA `0x1822CE880`）の実体（今回初めてbyte-exact確認、CONFIRMED）**:
```
for tabpos in 0..5:
    if !ArraysEqual(SelText[0][tabpos], SelText[1][tabpos]):  // 0x180a44720、bool比較関数
        return true   // 変更あり
return false           // 全タブ一致、変更なし
```
**これは`SelText[0]`（live）と`SelText[1]`（編集バッファ）の差分検知そのものであり、既存の用語「dirty check」に正確に対応する。** `cfgCopyFlagBakToGbl`/`cfgCopyFlagGblToBakTab`は今回改めて確認したところ、これらはdds3ConfigMainSteamの**別のフィールド（+0x30/+0x38/+0x40/+0x44、cctorで確認済みの"Flag"系フィールド群）を対象としており、SelText(+0x48)やconfig_dataとは無関係**であった（STRONGLY SUPPORTED、両関数とも今回のanchorスキャンで`+0x48`への到達が検出されなかったことと整合）。したがってこの2関数はTask2の主題（SelText⇔config_data間コピー）には直接寄与しない。

### 18.3 Task 3: startup X→Yの説明候補（大幅な進展）

以上の発見により、初めて一貫した動作モデルが得られた:

```
CONFIRMED（今回のセッションで判明した全体像）:

1. dds3ConfigProcessStart（Config画面を開いた瞬間）:
     SelText[1][tabpos] = config_data[tabpos]   （参照代入、alias化。全6タブ）

2. Config画面操作中、cfgSetBit/ChangeKey等がconfig_data[4][i]を書き換える
   （config_dataがSelText[1][4]とalias状態にあるため、SelText[1][4][i]からも
    同じ値が透過的に見える——ただしSelText[0][4]はまだ古いまま）

3. タブ確定操作（EndChoiceUpdate）:
     DatePrev(tabpos): SelText[1][tabpos] = SelText[0][tabpos]  (※これは「新規タブに入った直後」の
       同期であり、上記2との時系列関係は今回未確定。EndChoiceUpdateの呼出し順序詳細はUNRESOLVED)
     → tabpos=4ならChgConfigGamePadAll()も同時に呼ばれる

4. 変更確定（DateSave、EndChoiceUpdateまたはdds3UpdateConfigの毎フレームチェックから）:
     config_data[tabpos] = SelText[1][tabpos]
     SelText[0][tabpos] = SelText[1][tabpos]     ★ここでGetConfigGamePadの読み出し元が更新される★
```

**結論（HYPOTHESIS、実機観察との対応は未検証）**: 実機事実「起動初期X→探索後Y」が観測された際、**その間にConfigGamePad画面（Controller Key Config UI）が一度でも開かれ、何らかの変更確定操作（DateSave到達）が発生していれば、上記の経路で説明が可能**である。これは従来仮説にあった「reflection/vtable経由の未知のbackupinit=true呼出」を必要としない、**通常のUI操作のみで完結する説明**であり、既知の全関数のみで構成される点でより単純である。ただし:
```
UNRESOLVED:
実機テストの具体的な操作手順（Config画面を開いたか、
どのタブを触ったか、確定操作を行ったか）の記録が
本ドキュメント内に残っていないため、
この経路が実際にX→Y遷移の原因だったと断定はできない。
「Config画面に一切触れていない」ことが実機側で確認できれば、
この仮説はFALSIFIEDとなり、依然として未知の経路
（cfginitialize経由等、§17.2）を探す必要がある。
```

### 18.4 Task 4: metadata型矛盾の状態

今回、Il2CppType blobレベルでの再検証（typeIndex=36857の実デコード）までは行っていない（global-metadata.datのヘッダに解析済みの`types`テーブルが含まれておらず、GameAssembly.dll内の`Il2CppMetadataRegistration`構造体からのtypes配列解決という追加実装が必要なため、時間配分の都合で見送った——「無制限な探索を続けない」指示に従う）。

代わりに、今回の一連の解析（`DatePrev`/`DateSave`が汎用`Array.Copy`/`Array比較`ヘルパーを使っている、`cfgSetBit`/`GetConfigGamePad`のleaf読み書きが一貫して4byteストライドのInt32である）から、**間接的なSTRONGLY SUPPORTED評価を維持する**:
```
STRONGLY SUPPORTED（変更なし、追加の状況証拠のみ）:
SelTextの実体はInt32[][][]相当であり、
Cpp2ILの"String[][]"という型注釈は
decompilerの型推定誤りである可能性が高い。
今回のDatePrev/DateSave解析で使われる汎用の
Array.Copy/Array比較ヘルパーは型非依存(Array基底型)で
呼ばれているため、この追加証拠はString[][]説を
直接は否定も肯定もしない（中立）が、
leaf側の一貫したInt32 4byteアクセスパターンは
既存のCONFIRMED evidenceのまま揺るがない。

UNRESOLVED（変更なし）:
Il2CppType blobレベルでの正式デコードは未実施。
```

### 18.5 更新後のEvidence一覧（§17.4への追加差分）

| 項目 | 前回評価 | 今回評価 |
|---|---|---|
| SelText[0]/[1]とconfig_dataのalias関係 | 未調査 | **CONFIRMED: `dds3ConfigProcessStart`が`SelText[1][tab]`を`config_data[tab]`への参照代入(alias)にする。`[0]`は非alias** |
| DatePrevのコピー方向 | 未調査（読み出しのみ検出） | **CONFIRMED: `SelText[0][tab]→SelText[1][tab]`（live→編集バッファ）、かつtabpos=4で`ChgConfigGamePadAll()`を無条件呼出** |
| DateSaveのコピー方向 | 未調査（読み出しのみ検出） | **CONFIRMED: ループ1「`SelText[1][tab]→config_data[tab]`」、ループ2「`SelText[1][tab]→SelText[0][tab]`」。後者が探していたRuntime Apply/Sync Pathの実体** |
| SelText[0][4]（GAMEPAD、GetConfigGamePad読み出し元）への書込み経路 | UNRESOLVED（Task1/2を尽くしても未発見） | **CONFIRMED: `DateSave()`のループ2で発見。ただし`dds3UpdateConfig`（Config画面稼働中）経由でのみ到達可能、Config画面を一切開かない起動直後シーケンスでは到達しない** |
| cfgChkFlagChangeの実体 | 未調査（読み出しのみ検出） | **CONFIRMED: `SelText[0]`と`SelText[1]`の6タブ全比較によるdirty check** |
| cfgCopyFlagBakToGbl/GblToBakTab | 未調査 | **STRONGLY SUPPORTED: SelText/config_dataとは無関係の別フィールド(+0x30〜+0x44)を対象** |
| startup X→Y問題 | UNRESOLVED（backupinit=true経路未発見） | **HYPOTHESIS格上げ: Config画面操作＋DateSave到達で説明可能。ただし実機操作手順の記録がなく実証はUNRESOLVEDのまま** |

### 18.6 次回への申し送り

1. **最優先**: 実機側で「起動後、Config/Controller画面に一切触れずにフィールド探索へ入った場合でもX→Yが再現するか」を確認する（read-only実機実験、native改変不要）。再現すれば§18.3のHYPOTHESISはFALSIFIEDとなり、§17.2の`cfginitialize`系呼出元探索（vtable経由等）に戻る必要がある。再現しなければ（＝Config画面操作が必須なら）本セッションの発見でX→Y問題は実質的に解決したとみなせる。
2. `EndChoiceUpdate`内での`DatePrev`呼出しと`config_data`書込み（`ChangeKey`等）の時系列順序を確定する（現状は「同一関数内のどこかで両方起きる」ことしか分かっていない）。
3. Task4のIl2CppType blob正式デコード（`Il2CppMetadataRegistration.types`配列の解決）は引き続き持ち越し。

**静的解析が行き詰まった場合の最小read-only実機実験（今回の最終提案）**:
- 起動後、一度もController/GamePad Config画面を開かずにフィールド探索状態へ入り、`GetConfigGamePad(7)`の値を確認する。X(raw12)のままであれば§18.3のモデルが正しいことの強い傍証となる。次にController Config画面を一度開いて何も変更せず閉じ、再度確認する（`dds3ConfigProcessStart`のalias化のみでは`[0]`は変化しない設計であるため、これでもXのままのはずという予測が立てられる——これ自体が仮説の検証になる）。最後にキーを1つ変更して確定操作を行い、再度確認してYに変化するかを見る。この3段階観察により、DateSave到達が本当に必要十分条件かをread-only（既存のゲームプレイ操作の範囲内）で検証できる。

## 19. 実機テスト結果（2026-09-23、診断ログによるSTEP1〜3の実測）— §18.3 HYPOTHESISのFALSIFIED

`src/GameBindingDiagnosticProbe.cs`（read-only診断、`GetConfigGamePad(7)`を60frameごとにpolling、`FieldDashPatch.IsExplorationActive`遷移時は即時追加polling、`[NMC-GAMECFG-DIAG]`prefix、`ModernControllerApi`の既存snapshot処理とは完全独立）を新規実装し、deploy DLL SHA-256 `F5F7BD624850BF012869D1EC86DD050D5774C14C43C72617F128A6759F522CA1`（旧`ED7E893173119D5494E8A7666696650B115B9CE58ED7887E929D9AB467F0065C`から更新、旧DLLは`NocturneModernController.dll.pre-diag-backup-20260923_223113`としてMods配下に保全）で実機テストを実施した。**引き続き診断コード自体は読み取り専用（`ChangeKey`等の書込みAPI未使用、新規native hook無し）。**

### 19.1 Latest.logの実測タイムライン（CONFIRMED、byte-exactなログ抜粋）

```
[22:33:27.104] phase=STARTUP       index=7 raw=12 button=X elapsedMs=2852  exploration=False
[22:33:28.864] phase=VALUE_CHANGED index=7 before=12 after=11 button=Y elapsedMs=4611  exploration=False
[22:33:34.120] phase=FIELD_ENTER   index=7 raw=11 button=Y elapsedMs=9868  exploration=True
[22:33:34.143] phase=FIELD         index=7 raw=11 button=Y elapsedMs=9891  exploration=True
[22:33:34.465] [GameActionBindingSnapshot] GAME binding state ready after exploration became active (34/34); snapshot refreshed
[22:33:34.750] phase=FIELD_EXIT    index=7 raw=11 button=Y elapsedMs=10498 exploration=False
[22:33:35.266] phase=UNKNOWN       index=7 raw=11 button=Y elapsedMs=11014 exploration=False
[22:33:42.950] phase=FIELD_ENTER   index=7 raw=11 button=Y elapsedMs=18697 exploration=True
[22:33:43.098] phase=FIELD         index=7 raw=11 button=Y elapsedMs=18846 exploration=True
[22:33:43.765] phase=FIELD_EXIT    index=7 raw=11 button=Y elapsedMs=19512 exploration=False
[22:33:44.059] phase=UNKNOWN       index=7 raw=11 button=Y elapsedMs=19807 exploration=False
[22:33:57.279] phase=VALUE_CHANGED index=7 before=11 after=12 button=X elapsedMs=33027 exploration=False   ← STEP3前半（Xへ変更・確定）
[22:34:11.435] phase=VALUE_CHANGED index=7 before=12 after=11 button=Y elapsedMs=47183 exploration=False   ← STEP3後半（Yへ復元・確定）
```

ユーザー申告の実施内容: STEP1（Config画面を一切開かずセーブロード→フィールド到達）、STEP2（Config画面を開き無変更で閉じる）、STEP3（コマンドメニュー設定をXへ変更・確定後、Yへ復元・確定）。

### 19.2 決定的な新事実（CONFIRMED）

```
CONFIRMED（実機ログ、byte-exact）:
X→Yへの変化（before=12/after=11）は elapsedMs=4611 の時点で発生した。
これは FIELD_ENTER（elapsedMs=9868、exploration=Trueへの初回遷移）より
5257ms も前であり、exploration=False の状態で起きている。

STEP1の申告内容（Config画面を一切開いていない）と、
この変化がフィールド到達より数秒前に自動的に起きているという
タイミングの両方から、次のことが言える:

FALSIFIED: §18.3のHYPOTHESIS
「Config画面を開いてDateSave()に到達することがX→Y遷移の原因」
は、少なくとも今回観測された最初のX→Y遷移の説明としては誤りである。
Config画面へのユーザー操作は一切発生していない状況で、
起動後わずか約1.8秒（STARTUP初回サンプルからの経過）で
自動的にX→Yへ切り替わっている。
```

一方、**§18で発見した`DateSave()`のArray.Copy機構（`SelText[1]→SelText[0]`）自体が誤りだったわけではない**——STEP3の実測がこれを裏付けている:

```
CONFIRMED: STEP3で純正Config画面上でコマンドメニュー設定を
Yから別ボタン(X)へ変更・確定した瞬間(elapsedMs=33027)、
GetConfigGamePad(7)がbefore=11/after=12として即座に反映した。
さらに元のY(raw11)へ変更・確定した瞬間(elapsedMs=47183)にも
before=12/after=11として即座に反映した。
これは§18.2で解析した「確定操作(DateSave)がSelText[0][tab]を
SelText[1][tab]から更新する」という機構と完全に整合する
（CONFIRMED、実機で2回再現）。
```

**まとめると、`DateSave()`経由の`SelText[0][4]`更新機構は「ユーザーが純正Config画面で明示的に変更・確定した場合」には実測でも正しく機能する（CONFIRMED）。しかし、起動直後・フィールド到達前に自動的に発生する最初のX→Y遷移は、この機構とは別の、Config画面操作を必要としない自動経路によるものである（新事実、CONFIRMED）。**

### 19.3 更新された仮説（HYPOTHESIS、次回優先調査）

自動的なX→Y遷移が「Config画面を一度も開かず」「フィールド到達より前」に起きていることから、§17.2で発見済みだが呼出元未特定だった経路が再び最有力候補として浮上する:

```
HYPOTHESIS（優先度最上位、§17.2からの再浮上）:
起動〜セーブロード〜フィールド到達までの間のどこかで、
config_dataのロード(SteamOptionFile.LoadFile等、
既知でbackupinit=false)とは別に、
SelText[0][4]（および[1][4]）を直接更新する
自動apply経路が存在する。

最有力候補（未確定）:
- dds3ConfigMainSteam.cfginitialize(true分岐)
  （§17.2で静的呼出元0件と判明、vtable経由等の可能性を残したまま）
- ConfigAllDateInit()（backupinit=True既定、§17.2で静的参照0件、
  reflection/delegate経由の可能性を残したまま）
- あるいは今回のTask1/2いずれでも発見できなかった、
  全く別の経路

FALSIFIED（今回のログで否定された仮説）:
「Config画面操作(DateSave到達)が最初のX→Y遷移に必須」
```

elapsedMs=4611というタイミング（起動からわずか1.8秒後の初回成功read、フィールド到達の5秒以上前）は、**セーブファイルのロード処理そのものの直後、かつタイトル〜ロード画面の間**に発生している可能性が高い（STRONGLY SUPPORTED、時間的近接性からの推定）。次回はこの時間窓で実行される native 関数を的を絞って調査するのが有効と考えられる。

### 19.4 更新後のEvidence一覧（§18.5への追加差分）

| 項目 | §18時点の評価 | 今回（実機ログ後）の評価 |
|---|---|---|
| startup X→Y問題（Config画面操作が原因か） | HYPOTHESIS（DateSave到達で説明可能） | **FALSIFIED: 実機ログでConfig画面操作なし・フィールド到達前にX→Yが発生することを確認** |
| DateSave()のSelText[0]更新機構自体 | CONFIRMED（静的解析のみ） | **CONFIRMED（実機で2回再現、STEP3のX変更・Y復元の両方で即時反映を確認）** |
| 自動X→Y遷移の真の原因 | UNRESOLVED | **UNRESOLVED（変わらず）。ただしタイミング情報（起動後1.8秒、フィールド到達5秒以上前）が新たに判明し、§17.2のcfginitialize/ConfigAllDateInit reflection仮説が最有力候補として再浮上** |

### 19.5 次回への申し送り

1. **最優先**: elapsedMs換算でおよそ2.8秒〜4.6秒（STARTUP初回成功readからX→Y発生まで）の間に実行される native 関数を、`SteamOptionFile.LoadFile`/`FsSaveData.SteamConfigLocalLoad`の呼出元・その直後の処理から辿り直す。この時間窓は「セーブロード完了直後」である可能性が高く、§17.2で見つからなかった`cfginitialize(true)`または`ConfigAllDateInit()`の呼出元がこの近辺にある可能性がある。
2. 診断ログの`elapsedMs`はモード初期化からの相対時間であり、セーブロード操作の実時間との対応は未較正。次回実機テストでは「タイトル画面で"つづきから"を選んだ瞬間」等、ユーザー操作のタイミングも合わせてメモできるとより精密な絞り込みが可能。
3. `GameBindingDiagnosticProbe.cs`は一時診断コードのままリポジトリに残っている（削除・commit判断は次回以降）。

## 変更ファイル / git状態（2026-09-23分、追記）

このセッションでは`investigations/NATIVE_GAMEPAD_CONFIG_DATAFLOW_20260922.md`（本ファイル、§17として追記）のみを変更した。新規文書は作成していない。分析用の一時Pythonスクリプトはセッションのscratchpadディレクトリ（リポジトリ外）にのみ作成し、リポジトリには一切追加していない。既存ファイルの変更・削除・移動、build、deploy、commit、push、branch切替、stash pop、Ghidra Project本体への変更は一切行っていない。

## 変更ファイル / git状態（2026-09-23分・続き、§18追記時点）

本セッション（§18）でも同ファイルへの追記のみを行った。新規文書は作成していない。分析用の一時Pythonスクリプト（`meta_common.py`, `dump_func.py`, `find_callers.py`等、前回セッションから継続利用）はセッションのscratchpadディレクトリ（リポジトリ外）にのみ存在し、リポジトリには一切追加していない。既存ファイルの変更・削除・移動、build、deploy、commit、push、branch切替、stash pop、Ghidra Project本体への変更は一切行っていない。

## 変更ファイル / git状態（2026-09-23分・続き、§19追記時点）

本セッション（§19）では、実機テストの許可のもと`src/GameBindingDiagnosticProbe.cs`（新規、一時診断コード）を追加し、`src/ModMain.cs`へ1行（`GameBindingDiagnosticProbe.Sample();`の呼出）を追加した。core/settings/testsをclean build（いずれも0エラー0警告）し、既存deploy DLLをバックアップ（`Mods/NocturneModernController.dll.pre-diag-backup-20260923_223113`）した上でCore DLLのみdeploy（deploy後SHA-256とビルド出力の一致を確認済み）。Settingsは無変更のため再deployせず。純正GAME設定への書込み・ChangeKey等の実行・セーブデータ改変・commit・push・branch切替・stash操作は一切行っていない。

## 20. 自動X→Y遷移の発生元特定（2026-09-23、続き）— CONFIRMED、決定的な解決

前回（§19）の実機ログでFALSIFIEDとなった「Config画面操作が最初のX→Y遷移に必須」という仮説を受け、Config画面を一切経由しない自動apply経路を再探索した。**引き続きANALYSIS ONLY。native設定へのwrite・新規hook・build・deploy・commit・push・stash操作・branch切替は一切行っていない（開始前にHEAD=`f9624108f1cccbacaa5c8e7ed697b2d7b80b7d81`・detached・git status・stashを再確認、既存差分に変化なし、CONFIRMED）。**

### 20.1 手法上の反省点（今回の突破口）

§17.1のteint追跡ツール（`trace_writers.py`）は「タaintされたレジスタが**関数内で直接メモリstore命令のdestになる**」パターンのみを検出しており、**「taintされたレジスタがそのままcall命令の引数として渡され、呼出先の中で実際のstoreが行われる」パターン（`cfgSetBit`と同型の間接write）を原理的に見逃していた**（§17.1で明記済みの既知の限界）。今回はこの限界を踏まえ、`SteamOptionFile.LoadFile`→`FsSaveData.SteamConfigLocalLoad`→その呼出元・呼出先を手動でbyte-exactに辿り直した。

### 20.2 Task 1: ロード・初期化経路の全体像（CONFIRMED、決定的）

**`FsSaveData.SteamConfigLocalLoad`（VA `0x182706BE0`）の実体（新規解析、CONFIRMED）**:
```
1. SteamOptionFile.LoadFile(slot, path, 0) を呼び、セーブファイルの生データ(rsi)を取得
2. rsi==null（セーブなし）の場合:
   → ConfigAllDateInit2Tab(tabpos=0..5, ref &config_data, backupinit=false) を6回呼ぶ
     （§16.3で確認済みの"既知8箇所のbackupinit=false"に含まれる4箇所がこれ）
   → +0x48/SelTextには一切到達しない（backupinit=false固定のため、既知の結論通り）
3. rsi!=null（セーブあり）の場合:
   → 0x1813fe080でrsiの内容をローカルバッファ(rdi, 0x600byte=1536byte)へデコード
   → rdi[tab*0x100 .. ] を6回に分けて Array.Copy(source=rdi+tab*0x100, dest=FsSaveData.this.config_data[tab]（[rbx+0x20]系フィールド）, length=0x100)
     ※この時点でCONFIRMED: config_data[N]への実書込みはbackupinitの条件と無関係に発生する
       （§15.2の「1. 呼出元から渡されたconfig_data自体への書込みは常時実行」という
       ConfigAllDateInit2Tabの仕様と整合するが、今回のLoadFile内の直接Array.Copyは
       ConfigAllDateInit2Tabを経由しない別経路であることが新たに判明）
   → その他、Display関連の値クランプ処理等
   → +0x48/SelTextには、この関数内では到達しない（CONFIRMED、anchor 0x182E4EBA0への
     参照が本関数に0件であることを機械的に再確認済み）
```

**`FsSaveData.SteamConfigLocalLoad`はここまでの範囲では`SelText`を一切更新しない**——このためこの関数単体を追う限り自動X→Yは説明できなかった。

**caller chain（CONFIRMED、`.text`全体の網羅的E8/E9スキャンで再確認）**:
```
dds3DefaultMain.Start()（Unity lifecycle）
  → FsSaveData.SteamConfigLocalInit()
    → FsSaveData.SteamConfigLocalLoad()   ← 上記、config_data(FsSaveData local)まで反映
```

### 20.3 Task 2: SelText[0][4]自動更新経路 — `FsSaveData.SteamConfigLocalCopy`が真の実体（CONFIRMED、決定的）

`FsSaveData.SteamConfigLocalCopy`（VA `0x182706250`）を今回はじめて全命令byte-exactに読んだ。前回(§17.1)の機械的teint追跡ではこの関数の書込みを検出できていなかった（理由: taintされたレジスタが直接storeされず、`Array.Copy`系ヘルパー`0x1813F8650`への**引数**として渡されているため）。

**`SteamConfigLocalCopy(FsSaveData this, Int32 tabpos, Boolean flag)`の実体（tabpos=-1で「全タブ」ループに入る、CONFIRMED）**:
```
for tab in 0..5:
    src = FsSaveData.this.config_data[tab]           ; ロード直後のローカル値（§20.2で説明した値）

    // COPY 1
    dst1 = dds3GlobalWork.Instance.config_data[tab]
    Array.Copy(source=src, destination=dst1, length=0x100)

    // COPY 2 ★★★ 今回発見した、探していたwriter ★★★
    dst2 = dds3ConfigMainSteam.SelText[0][tab]          ; = arr0[0][tab]、GetConfigGamePadの読み出し元
    Array.Copy(source=src, destination=dst2, length=0x100)

    // COPY 3
    dst3 = dds3ConfigMainSteam.SelText[1][tab]          ; = arr0[1][tab]
    Array.Copy(source=src, destination=dst3, length=0x100)
```

**CONFIRMED（byte-exact、3つの独立したArray.Copy呼出し、いずれも同一の`FsSaveData.config_data[tab]`をsourceとする）**: `SteamConfigLocalCopy`は`config_data`・`SelText[0]`・`SelText[1]`の3箇所へ**同時に**、ロード済みの値をコピーする。これは`tabpos`引数を`-1`で呼べば0〜5の全タブに対して実行される。**`tabpos=4`（GAMEPAD）はこの範囲に含まれる。**

**呼出元（CONFIRMED、網羅的E8/E9スキャン、2箇所のみ）**:
```
| caller | tabpos引数 | 用途 |
|---|---|---|
| dds3KernelMain.dds3FirstInit（VA 0x18221DEC0） | -1（全タブ） | ★起動時の自動初期化★ |
| dds3ConfigMainSteam.DefaultPrev（VA 0x1822BB0C0） | レジスタ由来（単一タブ） | Config画面の「デフォルトに戻す」操作、UI起因 |
```

**`dds3FirstInit`の呼出箇所（0x18221E32E）でのレジスタセットアップをbyte-exactに確認（CONFIRMED）**:
```
xor r9d, r9d
xor r8d, r8d          ; flag = 0
lea edx, [r9 - 1]      ; tabpos = -1  ← 全タブコピー
call SteamConfigLocalCopy
```

**これが結論である: `dds3FirstInit`はConfig画面と無関係に、起動シーケンス中に`SteamConfigLocalCopy(-1, false)`を呼び、`SelText[0][4]`（`GetConfigGamePad`の読み出し元）へロード済みのセーブ値を直接コピーする。これが自動X→Y遷移の実体である（CONFIRMED）。**

### 20.4 Task 3: 実行タイミングの根拠（CONFIRMED〜STRONGLY SUPPORTED）

**caller chain（CONFIRMED、網羅的E8/E9スキャン）**:
```
<InitLoad>d__119.MoveNext（コンパイラ生成のコルーチン/イテレータ、"InitLoad"という名前から
  起動時ロードシーケンスのcoroutineであることがSTRONGLY SUPPORTED）
  → dds3KernelMain.m_dds3KernelMainInit（VA 0x18221EB90）
    → dds3KernelMain.dds3FirstInit（VA 0x18221DEC0）
        → evtPicturePriProcessInit（無関係、イベント/画像システム初期化）
        → dds3GlobalWork.dds3GlobalWorkInit（VA 0x1824df730、グローバルシングルトン初期化）
        → FsSaveData.SteamConfigLocalCopy(tabpos=-1, flag=false)   ★SelText[0]/[1]/config_data 更新★
```

`<InitLoad>d__119.MoveNext`という命名（C#コンパイラが`IEnumerator InitLoad()`のようなコルーチン/イテレータメソッドに自動生成する型名パターン）から、**この一連の初期化は複数フレームにまたがる起動時ロードコルーチンの一部として実行されるとSTRONGLY SUPPORTEDに推定できる**。これは実機ログで観測された「起動後わずか1.8秒程度、フィールド到達より5秒以上前」というタイミング（§19.1）と矛盾しない（STRONGLY SUPPORTED、時間的整合性のみからの推定であり、コルーチンの正確なフレーム位置は未確認）。

`dds3DefaultMain.Start()`（§20.2、config_dataへのロード）と`<InitLoad>d__119.MoveNext`（本節、SelTextへのコピー）が同一の起動シーケンス内でこの順序（Load→Copy）で実行されることを保証する直接的な証拠（Unity実行順序の確定）は今回得られていない（UNRESOLVED、ただし2回の独立した実機A/B/A的観測（§17のGameBindingProbe、§19の今回の診断）でX→Yが安定して再現しており、順序が偶然に依存する不安定な現象ではないことが実質的な傍証となっている）。

### 20.5 Task 3: readiness改善の可能性の検討（結論: 現時点での安全な代替案なし、変更提案せず）

ユーザー指示どおり、**現行の`FieldDashPatch.IsExplorationActive`によるreadiness判定は変更を提案しない**。

検討した代替候補と却下理由:
```
候補A: dds3KernelMain/dds3GlobalWorkの「初期化済み」フラグを直接読む
  → 該当フィールドの安全なmanaged接触経路（Il2Cpp型のpublicフィールド/プロパティとして
    露出されているか）を今回確認できていない（UNRESOLVED、次回調査候補）。
    仮に存在しても、新規の読み取りパスを追加する前に「本当に
    SteamConfigLocalCopy完了後にのみtrueになるか」を別途検証する必要がある。

候補B: <InitLoad>コルーチンの完了を検知する
  → コルーチン自体はUnity内部の実行状態であり、安全な既存の
    managed公開APIが無い限り、新規Harmonyパッチ（禁止事項）なしには
    検知できない。今回は見送り。

候補C（却下、ユーザー指示により明示的に排除）: 
  GetConfigGamePad(7)がraw11(Y)になったことをreadiness条件にする
  → ユーザーの最終設定はraw11(Y)とは限らない（プレイヤーが別のボタンを
    割り当てていれば別の値になる）ため、特定の値との一致で判定することは
    原理的に誤り。今回のTask3指示でも明確に却下されている。

現時点の結論: 安全な代替readiness signalは確認できておらず、
既存のexploration active条件を変更する根拠は無い（UNRESOLVED、変更提案なし）。
```

### 20.6 更新後のEvidence一覧（§19.4への追加差分）

| 項目 | §19時点の評価 | 今回の評価 |
|---|---|---|
| 自動X→Y遷移の真の原因 | UNRESOLVED（タイミング情報のみ判明） | **CONFIRMED: `dds3KernelMain.dds3FirstInit`→`FsSaveData.SteamConfigLocalCopy(tabpos=-1)`が`SelText[0][tab]`(tabpos=4を含む全6タブ)へ`Array.Copy`で直接書込む** |
| SteamConfigLocalLoadが自動X→Yの原因か | 未検証 | **FALSIFIED: `SteamConfigLocalLoad`自体は`config_data`(FsSaveDataローカル)までしか届かず、`SelText`には到達しない。実際の橋渡しは`SteamConfigLocalCopy`という別関数** |
| taint追跡ツールの網羅性 | 「関数境界を跨ぐ間接writeは検出不可」と明記済み | **CONFIRMED: 実際にこの限界により`SteamConfigLocalCopy`の書込みを§17.1で見逃していたことが今回判明。手動re-traceで確認** |
| readiness改善の可能性 | 未検討 | **UNRESOLVED: 安全な代替signalは今回確認できず。既存のexploration active条件の変更は提案しない** |

### 20.7 必要な場合のみ次の最小実機実験

現時点で自動X→Y遷移の発生源はCONFIRMEDまで到達したため、**追加の実機実験は必須ではない**。ただし、以下を確認したい場合は次のread-only実験が有効:
- `SteamConfigLocalCopy`が呼ばれるのが本当に一度きり（起動時のみ）かを確認するため、フィールド到達後にセーブ&ロード（タイトルへ戻らずリロード可能なら）を行い、GAME設定がリロードされた形跡（`GetConfigGamePad(7)`の一時的な再計算等）があるかを観察する。ただしTitle経由のリロードは通常`dds3TitleMain`から再度この初期化シーケンスを通ると考えられ、大きな新情報を生む可能性は低い（優先度低）。
