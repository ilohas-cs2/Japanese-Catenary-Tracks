# Japanese Catenary Tracks

Cities: Skylines II 向けの日本風架線付き線路 MOD の実行コードです。作者: **ilohas-cs2**。

## 配布版

[Paradox Mods](https://mods.paradoxplaza.com/mods/161773/Any) から導入します。

単線・複線、架線柱の種類に応じた配電線・き電線、線路選択アイコンに対応しています。

このリポジトリはコードと実行時設定を収録しています。ゲーム本体のライブラリ、モデル・テクスチャ・アイコンのパッケージ、制作資料は含みません。コードのビルドだけでは完全な MOD になりません。

## ビルド

- Windows、PowerShell、.NET 8 SDK
- Cities: Skylines II のインストール済みゲームライブラリ
- 検証に使用したゲームバージョン: 1.6.2f1

PowerShell でゲームの `Cities2_Data/Managed` フォルダーを指定します。次のパスは説明用のプレースホルダーです。

```powershell
./build.ps1 -GameManagedPath '<game-install-directory>/Cities2_Data/Managed'
```

`build/` に `JPCatenaryPrototype.dll` と設定ファイルが生成されます。スクリプトはゲームライブラリをコピーせず、ゲームへの自動インストールも行いません。

実際に動作させるには、対応する配布版のモデル・アイコンパッケージとリビジョンマーカーも必要です。配布版と異なるアセットを組み合わせると、ロードや表示が正常に動作しない場合があります。ゲームを終了してから作業し、ローカル版とサブスクライブ版を重複して有効にしないでください。

配布版・実行コードの AssemblyVersion は **0.3.0.9** です。専用線路がない街では架線補正用のオブジェクト走査を省略し、補正対象を専用線路と接続先に限定しました。状態が変化していない間は補正処理を省略します。

## 既知の課題

- 分岐や一部のモデル間接続で、架線・配電線の途切れや余分な短い線が見えることがあります。
- パンタグラフの追従は車両アセットの仕様に依存します。公式の初期車両では追従を確認していますが、一部の追加車両では隙間や食い込みが生じる場合があります。

## コードと素材の扱い

現時点では再利用ライセンスを設定していません。コードの公開は、モデル・テクスチャ・アイコンなどの素材の再配布や改変の許可を意味しません。ゲーム本体および第三者の素材の権利は各権利者に帰属します。

## English

Runtime source for a Japanese overhead-wire railway track mod for Cities: Skylines II, by **ilohas-cs2**.

Install the complete mod through the Paradox Mods link above. This repository contains runtime code and configuration only; game libraries, compiled models, textures, icons, and production references are not included.

To build, install the .NET 8 SDK and run the PowerShell command above with your game's `Cities2_Data/Managed` directory. Tested against game version 1.6.2f1. Output is written to `build/`; no game libraries are copied and nothing is installed automatically. Matching distribution assets and revision markers are required to run the mod. Close the game before replacing files and avoid loading local and subscribed copies simultaneously.

Package and runtime assembly version: **0.3.0.9**. Catenary correction skips object scanning in cities without custom tracks, scopes processing to custom tracks and their connections, and skips correction while state is unchanged. Wire discontinuities or stray segments can occur at junctions and some model transitions. Pantograph tracking depends on the vehicle asset and is not supported by every custom vehicle.

No reuse license has been granted at this time. Source publication does not grant permission to redistribute or modify model, texture, or icon assets. Game and third-party materials remain the property of their respective rights holders.
