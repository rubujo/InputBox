# 網頁 Git 提交與驗證 (Web Git & Validation)

- **Git 提交標準**：遵循 **Conventional Commits v1.0.0**。
- **訊息完整性**：
  - 必須包含 **Subject** (主旨) 與 **Body** (說明內容)。
  - 訊息一律使用 **正體中文** 撰寫。
- **文字檔編碼與換行**：
  - 網頁與規範相關文字檔一律使用 **UTF-8** 編碼。
  - 工作目錄中的文字檔換行一律使用 **CRLF**，並與 `.editorconfig`、`.gitattributes` 保持一致。
- **上線前檢查清單 (Checklist)**：
  - 驗證在 Light / Dark 模式下的視覺表現。
  - 驗證主題切換器三段模式（🖥️跟隨系統、☀️強制淺色、🌙強制深色）均正確套用 CSS 自訂屬性。
  - 確保所有語言標籤的 `lang` 屬性正確。
  - 檢查符號全形化與間距規範。
  - 確認編碼與換行未漂移為非 UTF-8 或非 CRLF。
  - 驗證所有 Landmark 結構正確，通過 A11y 檢測。
  - 執行 Playwright 測試套件（`npm test`）確認 44 項可及性與對比度驗證全部通過。
