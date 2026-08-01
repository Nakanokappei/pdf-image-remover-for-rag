# Privacy Policy

**PDF Image Remover for RAG**

Last updated: August 1, 2026

*日本語版はこのページの下部にあります。*

## Overview

PDF Image Remover for RAG is a desktop tool that removes images, repeated text and vector shapes from PDFs, and flattens overlapping objects into a picture, before those PDFs enter a retrieval pipeline. It is designed with privacy in mind and does not collect, transmit, or share any personal data.

## Data Collection

**We do not collect any personal data.** Specifically:

- No usage analytics or telemetry
- No crash reports sent by the app
- No user accounts or sign-in required
- No network connections made by the app, with one exception: choosing **Help → Online Manual** opens the manual page on GitHub in your default browser. That is your browser making the request, not the app.

## How your PDFs are handled

- Analysis, editing, and saving happen entirely on your PC.
- Original PDFs are never modified; results are written to a separate file you choose.
- PDF contents, file names, and file locations are never transmitted, uploaded, or shared.

### If you installed from the Microsoft Store

The app itself still sends nothing. Two things happen outside it.

Acquiring and installing the app is a transaction between you and Microsoft, recorded against your Microsoft account under Microsoft's privacy statement, not this app's. Windows may also report crashes and periods where the app stopped responding to Microsoft through Windows Error Reporting. That reporting is part of Windows rather than part of this app, and whether it happens is governed by your own Windows diagnostic data settings — under Settings > Privacy & security > Diagnostics & feedback — and by [Microsoft's privacy statement](https://privacy.microsoft.com/privacystatement).

Through Partner Center the developer receives summaries built from that: how many people acquired and installed the app, the ratings and reviews they left, and how many crashes and unresponsive periods occurred on which version, with the technical failure information Windows attaches to them. These summaries are aggregate — they describe the failure or the total, not the person — and are used only to fix defects and to see whether the app is worth continuing.

Nothing in any of this gives Microsoft or the developer your PDFs, their contents, their file names, or where they are stored. Those never leave your PC by any route.

## Data Stored Locally

The app stores the following data **only on your device**:

- **Window layout** — the window's position and size, and where you left the panel dividers, are saved to `%LOCALAPPDATA%\PdfImageRemoverForRag\window.json` so the app reopens looking the way you left it.
- **Operational logs** — `%LOCALAPPDATA%\PdfImageRemoverForRag\logs\` holds counts and timings, plus the type name of any exception. No file paths, no file names, no PDF content, no image data, no exception message text.
- **Thumbnails, while the app runs** — thumbnails are written to a temporary folder under `%LOCALAPPDATA%\PdfImageRemoverForRag\cache\` so that large documents do not have to be held in memory. That folder is deleted when the app exits.

None of this data is sent to any server, third party, or external service. All of it is removed when you uninstall the app or delete the folder above.

## Third-Party Services

This app does not use any third-party services, SDKs, or libraries that collect data. The two PDF libraries it bundles, PDFsharp and PdfPig, read and write files locally and make no network connections.

## Children's Privacy

This app does not collect any information from anyone, including children under the age of 13.

## Changes to This Policy

If this privacy policy is updated, the changes will be posted on this page with an updated date.

## Contact

If you have any questions about this privacy policy, please open an issue on the [GitHub repository](https://github.com/Nakanokappei/pdf-image-remover-for-rag/issues).

---

# プライバシーポリシー

**PDF Image Remover for RAG（RAG 用 PDF 画像除去ツール）**

最終更新日: 2026 年 8 月 1 日

## 概要

PDF Image Remover for RAG は、検索用途に取り込む前の PDF から、画像・繰り返し現れるテキスト・図形を取り除き、重なったオブジェクトをまとめて画像化するデスクトップツールです。本アプリはプライバシーに配慮して設計されており、個人データの収集・送信・共有を一切行いません。

## データの収集

**個人データは一切収集しません。** 具体的には以下のとおりです。

- 利用状況の分析やテレメトリの収集を行いません
- アプリがクラッシュレポートを送信することはありません
- ユーザーアカウントやサインインは不要です
- アプリからネットワーク接続を行いません。唯一の例外は、メニューの **[ヘルプ] → [オンラインマニュアル]** を選んだ場合で、既定のブラウザーで GitHub 上のマニュアルページが開きます。通信を行うのはブラウザーであり、本アプリではありません

## 開いた PDF の扱い

- 解析・編集・保存はすべてお使いの PC 内で完結します。
- 元の PDF ファイルは変更しません。結果は利用者が指定した別のファイルに書き出します。
- PDF の内容、ファイル名、保存場所を、外部に送信・保存・共有することはありません。

### Microsoft Store 版をご利用の場合

アプリ自身が何かを送信しない点は変わりません。アプリの外側で 2 つのことが起こります。

アプリの入手とインストールは、利用者と Microsoft との間の取引であり、Microsoft アカウントに紐づいて記録されます。これは本アプリではなく Microsoft のプライバシーステートメントが定めるものです。また、アプリのクラッシュや応答停止については、Windows が Windows エラー報告 (Windows Error Reporting) を通じて Microsoft に報告することがあります。これは本アプリの機能ではなく Windows の機能です。報告が行われるかどうかは、お使いの Windows の診断データ設定（[設定] > [プライバシーとセキュリティ] > [診断とフィードバック]）と [Microsoft のプライバシーステートメント](https://privacy.microsoft.com/privacystatement)によって決まり、本アプリが制御するものではありません。

開発者は、パートナーセンターを通じてそれらの集計情報を受け取ります。具体的には、何人が入手・インストールしたか、どのような評価やレビューが付いたか、どのバージョンでクラッシュや応答停止が何件発生したか、および Windows がそれに付随して記録する技術的な障害情報です。これらはすべて集計値であり、発生した障害や合計を示すものであって、利用者個人を特定するものではありません。不具合の修正と、開発を続ける価値があるかの判断のみに使用します。

以上のいずれによっても、利用者の PDF、その内容、ファイル名、保存場所が Microsoft や開発者に渡ることはありません。これらはどの経路でもお使いの PC から出ることはありません。

## ローカルに保存されるデータ

本アプリは、以下のデータを**お使いのデバイス内にのみ**保存します。

- **ウィンドウのレイアウト** — ウィンドウの位置・サイズと、パネルの仕切りの位置を `%LOCALAPPDATA%\PdfImageRemoverForRag\window.json` に保存し、次回起動時に同じ見た目で開きます。
- **動作ログ** — `%LOCALAPPDATA%\PdfImageRemoverForRag\logs\` に、処理件数などの数値と所要時間、およびエラー時の例外の種類名のみを記録します。ファイルのパス・ファイル名・PDF の内容・画像データ・エラーメッセージ本文は記録しません。
- **動作中のサムネイル** — 大きな文書をメモリに抱えないよう、`%LOCALAPPDATA%\PdfImageRemoverForRag\cache\` の一時フォルダーにサムネイル画像を書き出します。このフォルダーは終了時に削除されます。

これらのデータがサーバー、第三者、外部サービスに送信されることはありません。本アプリをアンインストールするか、上記フォルダーを削除すれば消えます。

## 第三者のサービス

本アプリは、データを収集する第三者のサービス、SDK、ライブラリを一切使用していません。同梱している 2 つの PDF ライブラリ PDFsharp と PdfPig は、ローカルでファイルを読み書きするのみで、ネットワーク接続を行いません。

## 児童のプライバシー

本アプリは、13 歳未満の児童を含め、いかなる利用者からも情報を収集しません。

## 本ポリシーの変更

本プライバシーポリシーを更新した場合は、更新日とともにこのページに変更内容を掲載します。

## お問い合わせ

本プライバシーポリシーに関するご質問は、[GitHub リポジトリ](https://github.com/Nakanokappei/pdf-image-remover-for-rag/issues) の Issue からお問い合わせください。
