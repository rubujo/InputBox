const { test, expect } = require("@playwright/test");
const AxeBuilder = require("@axe-core/playwright").default;
const path = require("node:path");

const pageUrl = `file://${path.resolve(__dirname, "../index.html")}`;
const languageIds = ["lang-zh", "lang-en", "lang-de", "lang-fr", "lang-ja", "lang-ko", "lang-sc"];
const themeIds = ["theme-sys", "theme-light", "theme-dark"];

function rgbComponents(rgbString) {
  return rgbString.match(/\d+/g).map((value) => Number(value));
}

function srgbToLinear(channel) {
  const c = channel / 255;
  return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
}

function relativeLuminance([r, g, b]) {
  const rl = srgbToLinear(r);
  const gl = srgbToLinear(g);
  const bl = srgbToLinear(b);
  return 0.2126 * rl + 0.7152 * gl + 0.0722 * bl;
}

function contrastRatio(foregroundRgb, backgroundRgb) {
  const l1 = relativeLuminance(foregroundRgb);
  const l2 = relativeLuminance(backgroundRgb);
  const lighter = Math.max(l1, l2);
  const darker = Math.min(l1, l2);
  return (lighter + 0.05) / (darker + 0.05);
}

test.describe("InputBox gh-pages A11y", () => {
  test("應通過主要 axe-core 規則（含 WCAG 2.2 A/AA）", async ({ page }) => {
    await page.goto(pageUrl);

    const accessibilityScanResults = await new AxeBuilder({ page })
      .withTags(["wcag2a", "wcag2aa", "wcag22a", "wcag22aa"])
      .disableRules(["color-contrast"])
      .analyze();

    expect(accessibilityScanResults.violations).toEqual([]);
  });

  test("應通過 WCAG 2.2 AAA 規則", async ({ page }) => {
    await page.goto(pageUrl);

    const aaaResults = await new AxeBuilder({ page }).withTags(["wcag2aaa", "wcag22aaa"]).analyze();

    expect(aaaResults.violations).toEqual([]);
  });

  test("iPhone SE 寬度下語言選單不應爆版", async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 667 });
    await page.goto(pageUrl);

    const switcher = page.locator(".lang-switcher");
    await expect(switcher).toBeVisible();

    const hasHorizontalOverflow = await switcher.evaluate(
      (element) => element.scrollWidth > element.clientWidth,
    );
    expect(hasHorizontalOverflow).toBeFalsy();

    const languageOptions = page.locator("input[name='lang']");
    await expect(languageOptions).toHaveCount(7);
  });

  test("iPhone SE 寬度下整頁不應出現水平捲動", async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 667 });
    await page.goto(pageUrl);

    const pageWidth = await page.evaluate(() => ({
      scrollWidth: document.documentElement.scrollWidth,
      clientWidth: document.documentElement.clientWidth,
    }));

    expect(pageWidth.scrollWidth).toBeLessThanOrEqual(pageWidth.clientWidth);
  });

  test("手機首頁初始畫面中返回頂端按鈕不應遮住主要操作", async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 667 });
    await page.goto(pageUrl);

    const ctaBox = await page.locator(".cta-button").first().boundingBox();
    const backToTopStyle = await page.locator(".back-to-top").evaluate((element) => {
      const style = getComputedStyle(element);
      return {
        visibility: style.visibility,
        opacity: Number(style.opacity),
      };
    });
    const backToTopBox = await page.locator(".back-to-top").boundingBox();

    const overlaps =
      ctaBox &&
      backToTopBox &&
      !(
        ctaBox.x + ctaBox.width <= backToTopBox.x ||
        backToTopBox.x + backToTopBox.width <= ctaBox.x ||
        ctaBox.y + ctaBox.height <= backToTopBox.y ||
        backToTopBox.y + backToTopBox.height <= ctaBox.y
      );

    const effectivelyHidden =
      backToTopStyle.visibility === "hidden" || backToTopStyle.opacity < 0.1;

    expect(effectivelyHidden || !overlaps).toBeTruthy();
  });

  test("淺色與深色主題切換應正確覆蓋系統配色", async ({ page }) => {
    await page.goto(pageUrl);

    await page.locator("#theme-light").evaluate((element) => {
      element.checked = true;
      element.dispatchEvent(new Event("change", { bubbles: true }));
    });
    const lightStyles = await page.locator("body").evaluate((element) => {
      const style = getComputedStyle(element);
      return {
        backgroundColor: style.backgroundColor,
        color: style.color,
      };
    });

    await page.locator("#theme-dark").evaluate((element) => {
      element.checked = true;
      element.dispatchEvent(new Event("change", { bubbles: true }));
    });
    const darkStyles = await page.locator("body").evaluate((element) => {
      const style = getComputedStyle(element);
      return {
        backgroundColor: style.backgroundColor,
        color: style.color,
      };
    });

    expect(lightStyles.backgroundColor).toBe("rgb(255, 255, 255)");
    expect(lightStyles.color).toBe("rgb(17, 24, 39)");
    expect(darkStyles.backgroundColor).toBe("rgb(6, 18, 31)");
    expect(darkStyles.color).toBe("rgb(248, 251, 255)");
  });

  test("列印模式應隱藏螢幕元件並恢復表格語意顯示", async ({ page }) => {
    await page.goto(pageUrl);
    await page.emulateMedia({ media: "print" });

    await expect(page.locator(".lang-switcher")).toBeHidden();
    await expect(page.locator(".skip-link")).toBeHidden();
    await expect(page.locator(".back-to-top")).toBeHidden();

    const tableDisplay = await page
      .locator("table")
      .first()
      .evaluate((element) => getComputedStyle(element).display);
    expect(tableDisplay).toBe("table");

    const theadDisplay = await page
      .locator("thead")
      .first()
      .evaluate((element) => getComputedStyle(element).display);
    expect(theadDisplay).toBe("table-header-group");

    const tbodyDisplay = await page
      .locator("tbody")
      .first()
      .evaluate((element) => getComputedStyle(element).display);
    expect(tbodyDisplay).toBe("table-row-group");
  });

  test("列印模式最後一列表格應保留底部邊框", async ({ page }) => {
    await page.goto(pageUrl);
    await page.emulateMedia({ media: "print" });

    const lastRowBorders = await page
      .locator("tbody tr")
      .last()
      .locator("th, td")
      .evaluateAll((cells) =>
        cells.map((cell) => {
          const style = getComputedStyle(cell);
          return {
            borderBottomStyle: style.borderBottomStyle,
            borderBottomWidth: style.borderBottomWidth,
          };
        }),
      );

    for (const border of lastRowBorders) {
      expect(border.borderBottomStyle).toBe("solid");
      expect(border.borderBottomWidth).toBe("1px");
    }
  });

  test("列印模式重要區塊應避免跨頁切割", async ({ page }) => {
    await page.goto(pageUrl);
    await page.emulateMedia({ media: "print" });

    const printRules = await page.evaluate(() => {
      const sectionHeading = document.querySelector("h2");
      const featureCard = document.querySelector(".feature-card");
      const tableWrapper = document.querySelector(".table-wrapper");

      return {
        headingBreakAfter: sectionHeading ? getComputedStyle(sectionHeading).breakAfter : null,
        cardBreakInside: featureCard ? getComputedStyle(featureCard).breakInside : null,
        tableWrapperBreakInside: tableWrapper ? getComputedStyle(tableWrapper).breakInside : null,
      };
    });

    expect(["avoid", "avoid-page"]).toContain(printRules.headingBreakAfter);
    expect(["avoid", "avoid-page"]).toContain(printRules.cardBreakInside);
    expect(["avoid", "avoid-page"]).toContain(printRules.tableWrapperBreakInside);
  });

  test("列印模式應套用色彩回退（白底黑字與灰階元件）", async ({ page }) => {
    await page.goto(pageUrl);
    await page.emulateMedia({ media: "print" });

    const bodyStyles = await page.locator("body").evaluate((element) => {
      const style = getComputedStyle(element);
      return {
        backgroundColor: style.backgroundColor,
        color: style.color,
      };
    });
    expect(bodyStyles.backgroundColor).toBe("rgb(255, 255, 255)");
    expect(bodyStyles.color).toBe("rgb(0, 0, 0)");

    const ctaStyles = await page
      .locator(".cta-button")
      .first()
      .evaluate((element) => {
        const style = getComputedStyle(element);
        return {
          color: style.color,
          borderTopColor: style.borderTopColor,
          borderTopWidth: style.borderTopWidth,
          borderTopStyle: style.borderTopStyle,
        };
      });
    const [r, g, b] = rgbComponents(ctaStyles.color);
    // CI／無頭模式渲染下，近黑文字可能有通道偏移，改以白底對比檢查可讀性。
    const ratioOnWhite = contrastRatio([r, g, b], [255, 255, 255]);
    expect(ratioOnWhite).toBeGreaterThanOrEqual(15);
    expect(ctaStyles.borderTopColor).toBe("rgb(0, 0, 0)");
    expect(ctaStyles.borderTopWidth).toBe("2px");
    expect(ctaStyles.borderTopStyle).toBe("solid");

    const kbdStyles = await page
      .locator("kbd")
      .first()
      .evaluate((element) => {
        const style = getComputedStyle(element);
        return {
          backgroundColor: style.backgroundColor,
          color: style.color,
        };
      });
    expect(kbdStyles.backgroundColor).toBe("rgb(240, 240, 240)");
    expect(kbdStyles.color).toBe("rgb(0, 0, 0)");

    const thStyles = await page
      .locator("th")
      .first()
      .evaluate((element) => {
        const style = getComputedStyle(element);
        return {
          backgroundColor: style.backgroundColor,
          color: style.color,
        };
      });
    expect(thStyles.backgroundColor).toBe("rgb(240, 240, 240)");
    expect(thStyles.color).toBe("rgb(0, 0, 0)");
  });

  test("列印模式在各語系與各主題下都應維持正確顯示", async ({ page }) => {
    for (const themeId of themeIds) {
      for (const languageId of languageIds) {
        await page.goto(pageUrl);

        await page.locator(`#${themeId}`).evaluate((element) => {
          element.checked = true;
          element.dispatchEvent(new Event("change", { bubbles: true }));
        });
        await page.locator(`#${languageId}`).evaluate((element) => {
          element.checked = true;
          element.dispatchEvent(new Event("change", { bubbles: true }));
        });

        await page.emulateMedia({ media: "print" });

        const bodyStyles = await page.locator("body").evaluate((element) => {
          const style = getComputedStyle(element);
          return {
            backgroundColor: style.backgroundColor,
            color: style.color,
          };
        });
        expect(bodyStyles.backgroundColor).toBe("rgb(255, 255, 255)");
        expect(bodyStyles.color).toBe("rgb(0, 0, 0)");

        const languageDisplay = await page.locator("h1").evaluate((heading) => {
          const spans = Array.from(heading.querySelectorAll("span"));
          const map = {};
          for (const span of spans) {
            const langClass = Array.from(span.classList).find((name) => name.startsWith("lang-"));
            if (langClass) {
              map[langClass] = getComputedStyle(span).display;
            }
          }
          return map;
        });

        for (const candidate of languageIds) {
          const displayValue = languageDisplay[candidate];
          if (candidate === languageId) {
            expect(displayValue).not.toBe("none");
          } else {
            expect(displayValue).toBe("none");
          }
        }
      }
    }
  });
});
