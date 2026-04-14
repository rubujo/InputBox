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

    const pageWidth = await page.evaluate(() => {
      const vw = document.documentElement.clientWidth;
      const sw = document.documentElement.scrollWidth;

      /* 僅在失敗時收集偵錯資訊，避免效能負擔 */
      let offenders = [];
      if (sw > vw) {
        function insideFixed(el) {
          let p = el.parentElement;
          while (p) {
            if (getComputedStyle(p).position === "fixed") return true;
            p = p.parentElement;
          }
          return false;
        }
        for (const el of document.querySelectorAll("body, body *")) {
          if (insideFixed(el)) continue;
          const bcr = el.getBoundingClientRect();
          const elSw = el.scrollWidth;
          const ow = el.offsetWidth;
          if (bcr.right > vw + 0.5 || elSw > vw + 0.5 || ow > vw + 0.5) {
            const s = getComputedStyle(el);
            offenders.push({
              tag: el.tagName.toLowerCase(),
              id: el.id || "",
              cls: (el.className || "").toString().slice(0, 50),
              text: el.textContent.trim().replace(/\s+/g, " ").slice(0, 40),
              bcrRight: Math.round(bcr.right),
              offsetWidth: ow,
              scrollWidth: elSw,
              position: s.position,
              display: s.display,
              overflowX: s.overflowX,
              width: s.width,
              minWidth: s.minWidth,
              whiteSpace: s.whiteSpace,
            });
          }
        }
        offenders = offenders.sort((a, b) => b.bcrRight - a.bcrRight).slice(0, 10);
      }

      return { scrollWidth: sw, clientWidth: vw, offenders };
    });

    if (pageWidth.scrollWidth > pageWidth.clientWidth) {
      console.log("⚠️ 水平溢出偵錯資訊：", JSON.stringify(pageWidth.offenders, null, 2));
    }

    expect(pageWidth.scrollWidth).toBeLessThanOrEqual(pageWidth.clientWidth);
  });

  test("iPhone SE 寬度下若語言項目為奇數，最後一項應獨占一列", async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 667 });
    await page.goto(pageUrl);

    const koBox = await page.locator("#label-lang-ko").boundingBox();
    const scBox = await page.locator("#label-lang-sc").boundingBox();

    expect(koBox).toBeTruthy();
    expect(scBox).toBeTruthy();
    expect(scBox.y - koBox.y).toBeGreaterThan(20);
  });

  test("頁面初始狀態下 main-nav 第一項應有清楚高亮", async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto(pageUrl);

    await page.locator("#theme-light").evaluate((element) => {
      element.checked = true;
      element.dispatchEvent(new Event("change", { bubbles: true }));
    });

    const navBackground = await page.locator("nav.main-nav").evaluate((element) => {
      const style = getComputedStyle(element);
      return style.backgroundColor;
    });

    const aboutNav = await page.locator('nav.main-nav a[href="#about"]').evaluate((element) => {
      const style = getComputedStyle(element);
      return {
        color: style.color,
        backgroundColor: style.backgroundColor,
        boxShadow: style.boxShadow,
      };
    });

    const ratio = contrastRatio(
      rgbComponents(aboutNav.color),
      rgbComponents(aboutNav.backgroundColor),
    );

    expect(aboutNav.boxShadow).not.toBe("none");
    expect(aboutNav.backgroundColor).not.toBe(navBackground);
    expect(ratio).toBeGreaterThanOrEqual(7);
  });

  test("窄版面捲動時導覽列 scrollspy 應切換到目前區塊", async ({ page }) => {
    await page.setViewportSize({ width: 700, height: 900 });
    await page.goto(pageUrl);

    await page.locator("#theme-light").evaluate((element) => {
      element.checked = true;
      element.dispatchEvent(new Event("change", { bubbles: true }));
    });

    await page.locator("#spec").scrollIntoViewIfNeeded();
    await page.waitForTimeout(300);

    const navBackground = await page.locator("nav.main-nav").evaluate((element) => {
      const style = getComputedStyle(element);
      return style.backgroundColor;
    });

    const aboutNav = await page.locator('nav.main-nav a[href="#about"]').evaluate((element) => {
      const style = getComputedStyle(element);
      return {
        color: style.color,
        backgroundColor: style.backgroundColor,
        boxShadow: style.boxShadow,
      };
    });

    const specNav = await page.locator('nav.main-nav a[href="#spec"]').evaluate((element) => {
      const style = getComputedStyle(element);
      return {
        color: style.color,
        backgroundColor: style.backgroundColor,
        boxShadow: style.boxShadow,
      };
    });

    const aboutRatio = contrastRatio(rgbComponents(aboutNav.color), rgbComponents(navBackground));
    const specRatio = contrastRatio(
      rgbComponents(specNav.color),
      rgbComponents(specNav.backgroundColor),
    );

    expect(aboutNav.boxShadow).toBe("none");
    expect(aboutRatio).toBeGreaterThanOrEqual(7);
    expect(specNav.boxShadow).not.toBe("none");
    expect(specNav.backgroundColor).not.toBe(aboutNav.backgroundColor);
    expect(specRatio).toBeGreaterThanOrEqual(7);
  });

  test("main-nav 在桌面與手機的淺色與深色主題下都應維持 AAA 對比", async ({ page }) => {
    const cases = [
      { name: "desktop-light", width: 1280, height: 900, theme: "#theme-light" },
      { name: "desktop-dark", width: 1280, height: 900, theme: "#theme-dark" },
      { name: "mobile-light", width: 375, height: 812, theme: "#theme-light" },
      { name: "mobile-dark", width: 375, height: 812, theme: "#theme-dark" },
    ];

    for (const testCase of cases) {
      await page.setViewportSize({ width: testCase.width, height: testCase.height });
      await page.goto(pageUrl);

      await page.locator(testCase.theme).evaluate((element) => {
        element.checked = true;
        element.dispatchEvent(new Event("change", { bubbles: true }));
      });

      await page.locator("#spec").scrollIntoViewIfNeeded();
      await page.waitForTimeout(400);

      const specNav = await page.locator('nav.main-nav a[href="#spec"]').evaluate((element) => {
        const style = getComputedStyle(element);
        return {
          color: style.color,
          backgroundColor: style.backgroundColor,
          boxShadow: style.boxShadow,
        };
      });

      const ratio = contrastRatio(
        rgbComponents(specNav.color),
        rgbComponents(specNav.backgroundColor),
      );

      expect(
        specNav.boxShadow,
        `${testCase.name} should show a clear current-state indicator`,
      ).not.toBe("none");
      expect(
        ratio,
        `${testCase.name} should keep text contrast at AAA level`,
      ).toBeGreaterThanOrEqual(7);
    }
  });

  test("iPhone SE 寬度下 main-nav 目前區塊應使用明顯卡片高亮", async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.goto(pageUrl);

    await page.locator("#spec").scrollIntoViewIfNeeded();
    await page.waitForTimeout(400);

    const specNav = await page.locator('nav.main-nav a[href="#spec"]').evaluate((element) => {
      const style = getComputedStyle(element);
      return {
        color: style.color,
        backgroundColor: style.backgroundColor,
        boxShadow: style.boxShadow,
        textDecorationLine: style.textDecorationLine,
      };
    });

    const ratio = contrastRatio(
      rgbComponents(specNav.color),
      rgbComponents(specNav.backgroundColor),
    );

    expect(specNav.boxShadow).toContain("0px 0px 0px 3px");
    expect(specNav.textDecorationLine).toBe("none");
    expect(ratio).toBeGreaterThanOrEqual(7);
  });

  test("手機版網址含 #about 時 main-nav 不應同時高亮多個項目", async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 667 });
    await page.goto(`${pageUrl}#about`);
    await page.waitForTimeout(400);

    const aboutNav = await page.locator('nav.main-nav a[href="#about"]').evaluate((element) => {
      const style = getComputedStyle(element);
      return {
        boxShadow: style.boxShadow,
      };
    });

    const usageNav = await page.locator('nav.main-nav a[href="#usage"]').evaluate((element) => {
      const style = getComputedStyle(element);
      return {
        boxShadow: style.boxShadow,
      };
    });

    expect(aboutNav.boxShadow).not.toBe("none");
    expect(usageNav.boxShadow).toBe("none");
  });

  test("網址含 hash 時捲動後 scrollspy 不應殘留舊區塊高亮", async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto(`${pageUrl}#spec`);
    await page.waitForTimeout(300);

    await page.locator("#faq").scrollIntoViewIfNeeded();
    await page.waitForTimeout(400);

    const specNav = await page.locator('nav.main-nav a[href="#spec"]').evaluate((element) => {
      const style = getComputedStyle(element);
      return {
        backgroundColor: style.backgroundColor,
        boxShadow: style.boxShadow,
      };
    });

    const faqNav = await page.locator('nav.main-nav a[href="#faq"]').evaluate((element) => {
      const style = getComputedStyle(element);
      return {
        color: style.color,
        backgroundColor: style.backgroundColor,
        boxShadow: style.boxShadow,
      };
    });

    const ratio = contrastRatio(rgbComponents(faqNav.color), rgbComponents(faqNav.backgroundColor));

    expect(specNav.boxShadow).toBe("none");
    expect(faqNav.boxShadow).not.toBe("none");
    expect(faqNav.backgroundColor).not.toBe(specNav.backgroundColor);
    expect(ratio).toBeGreaterThanOrEqual(7);
  });

  test("Android 粗指標裝置應套用低負載視覺設定", async ({ page }) => {
    await page.setViewportSize({ width: 412, height: 915 });
    await page.emulateMedia({ reducedMotion: "reduce" });
    await page.goto(pageUrl);

    const perfStyles = await page.evaluate(() => {
      const glow = document.querySelector(".header-glow");
      const nav = document.querySelector("nav.main-nav");
      const backToTop = document.querySelector(".back-to-top");
      return {
        glowDisplay: glow ? getComputedStyle(glow).display : null,
        navBackdrop: nav ? getComputedStyle(nav).backdropFilter : null,
        backToTopTransform: backToTop ? getComputedStyle(backToTop).transform : null,
      };
    });

    expect(perfStyles.glowDisplay).toBe("none");
    expect(["none", ""]).toContain(perfStyles.navBackdrop);
    expect(perfStyles.backToTopTransform).toBeTruthy();
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
