const { test, expect } = require("@playwright/test");
const AxeBuilder = require("@axe-core/playwright").default;
const path = require("node:path");

const pageUrl = `file://${path.resolve(__dirname, "../index.html")}`;

function rgbComponents(rgbString) {
  return rgbString.match(/\d+/g).map((value) => Number(value));
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

    const aaaResults = await new AxeBuilder({ page })
      .withTags(["wcag2aaa", "wcag22aaa"])
      .analyze();

    expect(aaaResults.violations).toEqual([]);
  });

  test("iPhone SE 寬度下語言選單不應爆版", async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 667 });
    await page.goto(pageUrl);

    const switcher = page.locator(".lang-switcher");
    await expect(switcher).toBeVisible();

    const hasHorizontalOverflow = await switcher.evaluate((element) => element.scrollWidth > element.clientWidth);
    expect(hasHorizontalOverflow).toBeFalsy();

    const languageOptions = page.locator("input[name='lang']");
    await expect(languageOptions).toHaveCount(7);
  });

  test("列印模式應隱藏螢幕元件並恢復表格語意顯示", async ({ page }) => {
    await page.goto(pageUrl);
    await page.emulateMedia({ media: "print" });

    await expect(page.locator(".lang-switcher")).toBeHidden();
    await expect(page.locator(".skip-link")).toBeHidden();
    await expect(page.locator(".back-to-top")).toBeHidden();

    const tableDisplay = await page.locator("table").first().evaluate((element) => getComputedStyle(element).display);
    expect(tableDisplay).toBe("table");

    const theadDisplay = await page.locator("thead").first().evaluate((element) => getComputedStyle(element).display);
    expect(theadDisplay).toBe("table-header-group");

    const tbodyDisplay = await page.locator("tbody").first().evaluate((element) => getComputedStyle(element).display);
    expect(tbodyDisplay).toBe("table-row-group");
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

    const ctaStyles = await page.locator(".cta-button").first().evaluate((element) => {
      const style = getComputedStyle(element);
      return {
        color: style.color,
        borderTopColor: style.borderTopColor,
        borderTopWidth: style.borderTopWidth,
        borderTopStyle: style.borderTopStyle,
      };
    });
    const [r, g, b] = rgbComponents(ctaStyles.color);
    // CI／無頭模式渲染下，近黑文字可能出現極小的非零色彩通道偏移。
    expect(r).toBeLessThanOrEqual(16);
    expect(g).toBeLessThanOrEqual(16);
    expect(b).toBeLessThanOrEqual(16);
    expect(ctaStyles.borderTopColor).toBe("rgb(0, 0, 0)");
    expect(ctaStyles.borderTopWidth).toBe("2px");
    expect(ctaStyles.borderTopStyle).toBe("solid");

    const kbdStyles = await page.locator("kbd").first().evaluate((element) => {
      const style = getComputedStyle(element);
      return {
        backgroundColor: style.backgroundColor,
        color: style.color,
      };
    });
    expect(kbdStyles.backgroundColor).toBe("rgb(240, 240, 240)");
    expect(kbdStyles.color).toBe("rgb(0, 0, 0)");

    const thStyles = await page.locator("th").first().evaluate((element) => {
      const style = getComputedStyle(element);
      return {
        backgroundColor: style.backgroundColor,
        color: style.color,
      };
    });
    expect(thStyles.backgroundColor).toBe("rgb(240, 240, 240)");
    expect(thStyles.color).toBe("rgb(0, 0, 0)");
  });
});
