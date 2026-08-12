using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using TMobileScraper.Options;
using TMobileScraper.Models;

namespace TMobileScraper.Helpers;

public static class PlaywrightScraperHelper
{
    private static readonly SemaphoreSlim BrowserLock = new(1, 1);
    private static readonly SemaphoreSlim InstallLock = new(1, 1);
    private static bool _browsersReady;

    public static async Task<(bool Success, string Message, Dictionary<string, List<Dictionary<string, object?>>> Data)> ScrapeTMobileDealerOrderingAsync(
        ScrapingWebsite website, string filter, ScrapingOptions options, CancellationToken cancellationToken = default)
    {
        const string catalogLinkText = "Catalog";
        var pricePattern = new Regex(@"\$?\s*([\d,]+(?:\.\d{2})?)", RegexOptions.Compiled);
        var materialNumberPattern = new Regex(@"^\d{8,}$", RegexOptions.Compiled);

        await BrowserLock.WaitAsync(cancellationToken);
        IPlaywright? playwright = null;
        IBrowser? browser = null;
        IPage? page = null;

        async Task DoStep(IPage stepPage, string step, Func<Task> action)
        {
            try { await action(); }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                await SaveDebugSnapshotAsync(stepPage, step.Replace(' ', '-').ToLowerInvariant());
                throw new InvalidOperationException($"Scraping failed at step '{step}': {ex.Message}", ex);
            }
        }

        async Task<T> DoStepAsync<T>(IPage stepPage, string step, Func<Task<T>> action)
        {
            try { return await action(); }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                await SaveDebugSnapshotAsync(stepPage, step.Replace(' ', '-').ToLowerInvariant());
                throw new InvalidOperationException($"Scraping failed at step '{step}': {ex.Message}", ex);
            }
        }

        try
        {
            (playwright, browser, page) = await InitializeBrowserAsync(options, cancellationToken);

            await DoStep(page, "Open login page", () =>
                page.GotoAsync(website.website_url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded }));

            if ((await page.TitleAsync()).Contains("Access Denied", StringComparison.OrdinalIgnoreCase))
                return (false, "The portal blocked this server's network address.", []);

            await DoStep(page, "Login", () => LoginAsync(page, website, options.TimeoutSeconds));

            if ((await page.TitleAsync()).Contains("Access Denied", StringComparison.OrdinalIgnoreCase))
                return (false, "The portal blocked this server's network address.", []);

            if (await page.Locator("#password:visible, input[name='nolog_password']:visible").CountAsync() > 0)
                return (false, "Login failed. Check username/password in scraping_websites table.", []);

            await DoStep(page, "Wait for portal", () => WaitForPortalReadyAsync(page, Math.Min(options.TimeoutSeconds, 45)));

            var catalogPage = await DoStepAsync(page, "Open Catalog tab", () =>
                OpenCatalogAsync(page, catalogLinkText, options.TimeoutSeconds));

            await WaitForProductsAsync(catalogPage, 10);

            var categories = ParseCategoryFilters(filter);
            var exportByCategory = new Dictionary<string, List<Dictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);
            var summaryParts = new List<string>(categories.Count);

            foreach (var category in categories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await DoStep(catalogPage, $"Open {category}", () => NavigateToCategoryAsync(catalogPage, category));
                await WaitForProductsAsync(catalogPage, 10);

                var rows = await DoStepAsync(catalogPage, $"Scrape {category}", () => ScrapeProductsAsync(catalogPage, category));
                exportByCategory[category] = rows;
                summaryParts.Add($"{rows.Count} {category}");
            }

            if (exportByCategory.Values.Sum(static rows => rows.Count) == 0)
            {
                await SaveDebugSnapshotAsync(catalogPage, "no-products");
                if (!options.Headless && options.DebugPauseSeconds > 0)
                    await catalogPage.WaitForTimeoutAsync(options.DebugPauseSeconds * 1_000);

                return (false, $"No products were found for: {string.Join(", ", categories)}.", []);
            }

            if (!options.Headless)
                await page.WaitForTimeoutAsync(500);

            return (true, $"{string.Join(" + ", summaryParts)} products scraped successfully.", exportByCategory);
        }
        catch (Exception ex)
        {
            if (page is not null)
                await SaveDebugSnapshotAsync(page, "error");

            if (page is not null && !options.Headless && options.DebugPauseSeconds > 0)
                await page.WaitForTimeoutAsync(options.DebugPauseSeconds * 1_000);

            var message = ex.Message.StartsWith("Scraping failed:", StringComparison.Ordinal)
                ? ex.Message
                : $"Scraping failed: {ex.Message}";

            return (false, message, []);
        }
        finally
        {
            if (browser is not null)
                await browser.CloseAsync();

            playwright?.Dispose();
            BrowserLock.Release();
        }

        async Task LoginAsync(IPage loginPage, ScrapingWebsite site, int timeoutSeconds)
        {
            await loginPage.Locator("#userid, input[name='UserId']").First.FillAsync(site.username);
            await loginPage.Locator("#password, input[name='nolog_password']").First.FillAsync(site.password);
            await loginPage.Locator("input[name='AgreeTerms']").CheckAsync();
            await loginPage.Locator("a[name='login']").ClickAsync();

            await loginPage.WaitForFunctionAsync(
                """
                () => {
                  const pwd = document.querySelector("#password, input[name='nolog_password']");
                  const visible = pwd && (pwd.offsetParent !== null || pwd.getClientRects().length > 0);
                  return !(location.href.toLowerCase().includes('init.do') && visible);
                }
                """,
                null,
                new PageWaitForFunctionOptions { Timeout = timeoutSeconds * 1_000 });

            try
            {
                await loginPage.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 20_000 });
            }
            catch (TimeoutException)
            {
                await loginPage.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            }

            await loginPage.WaitForTimeoutAsync(2000);
        }

        async Task WaitForPortalReadyAsync(IPage portalPage, int timeoutSeconds)
        {
            await portalPage.WaitForFunctionAsync(
                """
                () => {
                  const collectDocs = (win, docs = []) => {
                    try { docs.push(win.document); for (const frame of win.frames) collectDocs(frame, docs); } catch {}
                    return docs;
                  };
                  const docs = collectDocs(window);
                  if (docs.some(doc => doc.querySelector("ul.left-tabs a, ul.navigation-1 a, .header a, .header-links a"))) return true;
                  if (docs.length > 1) return true;
                  return docs.some(doc => !doc.querySelector("a[name='login']") && !doc.querySelector("#password, input[name='nolog_password']"));
                }
                """,
                null,
                new PageWaitForFunctionOptions { Timeout = timeoutSeconds * 1_000 });
        }

        async Task<IPage> OpenCatalogAsync(IPage portalPage, string linkText, int timeoutSeconds)
        {
            var deadline = DateTime.UtcNow.AddSeconds(Math.Min(timeoutSeconds, 30));
            var linkPattern = new Regex(Regex.Escape(linkText), RegexOptions.IgnoreCase);

            while (DateTime.UtcNow < deadline)
            {
                var existing = await FindPageWithProductsAsync(portalPage);
                if (existing is not null)
                    return existing;

                if (await CatalogLinkVisibleAsync(portalPage, linkPattern))
                {
                    var popupTask = portalPage.Context.WaitForPageAsync(new() { Timeout = 8_000 });
                    if (await TryClickCatalogLinkAsync(portalPage, linkPattern))
                    {
                        try
                        {
                            await popupTask;
                            await portalPage.WaitForTimeoutAsync(3000);
                        }
                        catch (TimeoutException)
                        {
                            await portalPage.WaitForTimeoutAsync(3000);
                        }

                        return await FindPageWithProductsAsync(portalPage) ?? portalPage;
                    }
                }

                await portalPage.WaitForTimeoutAsync(750);
            }

            throw new InvalidOperationException($"Catalog link '{linkText}' was not found after login.");
        }

        async Task<bool> CatalogLinkVisibleAsync(IPage portalPage, Regex pattern)
        {
            foreach (var frame in portalPage.Frames)
            {
                if (await frame.Locator("ul.left-tabs a, ul.navigation-1 a, .header a, a")
                        .Filter(new() { HasTextRegex = pattern }).CountAsync() > 0)
                    return true;
            }

            return false;
        }

        async Task<IPage?> FindPageWithProductsAsync(IPage portalPage)
        {
            foreach (var candidate in portalPage.Context.Pages)
            {
                if (await GetProductCountAsync(candidate) > 0)
                    return candidate;
            }

            return null;
        }

        async Task<bool> TryClickCatalogLinkAsync(IPage portalPage, Regex pattern)
        {
            foreach (var frame in portalPage.Frames)
            {
                var link = frame.Locator("ul.left-tabs a, ul.navigation-1 a, .header a, .header-links a, a")
                    .Filter(new() { HasTextRegex = pattern })
                    .First;

                if (await link.CountAsync() > 0)
                {
                    await link.ClickAsync(new LocatorClickOptions { Force = true });
                    return true;
                }
            }

            return false;
        }

        List<string> ParseCategoryFilters(string? categoryFilter)
        {
            if (string.IsNullOrWhiteSpace(categoryFilter))
                return ["Phones"];

            return categoryFilter
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => x.Equals("COP", StringComparison.OrdinalIgnoreCase) ? "CPO" : x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        async Task NavigateToCategoryAsync(IPage catalogPg, string category)
        {
            if (category.Equals("CPO", StringComparison.OrdinalIgnoreCase))
            {
                await ClickCategoryAsync(catalogPg, "Phones");
                await catalogPg.WaitForTimeoutAsync(1500);
            }

            var fingerprintBefore = await GetProductFingerprintAsync(catalogPg);
            if (!await ClickCategoryAsync(catalogPg, category))
                throw new InvalidOperationException($"Catalog category '{category}' was not found in the sidebar.");

            await WaitForCategoryChangeAsync(catalogPg, category, fingerprintBefore);
        }

        async Task<string> GetProductFingerprintAsync(IPage catalogPg)
        {
            foreach (var frame in catalogPg.Frames)
            {
                var item = frame.Locator(".catalauge-item-holder").First;
                if (await item.CountAsync() > 0)
                {
                    return await item.EvaluateAsync<string>(
                        "el => (el.innerText || '').replace(/\\s+/g, ' ').trim().slice(0, 160)");
                }

                var row = frame.Locator("table.product-list tr.odd, table.product-list tr.even").First;
                if (await row.CountAsync() > 0)
                {
                    return await row.EvaluateAsync<string>(
                        "el => (el.innerText || '').replace(/\\s+/g, ' ').trim().slice(0, 160)");
                }
            }

            return string.Empty;
        }

        async Task<bool> IsCategoryActiveAsync(IPage catalogPg, string category)
        {
            return await catalogPg.EvaluateAsync<bool>(
                """
                (text) => {
                  const target = (text || '').trim().toLowerCase();
                  const norm = (v) => (v || '').replace(/\s+/g, ' ').trim().toLowerCase();
                  const collectDocs = (win, docs = []) => {
                    try { docs.push(win.document); for (const f of win.frames) collectDocs(f, docs); } catch {}
                    return docs;
                  };
                  for (const doc of collectDocs(window)) {
                    for (const span of doc.querySelectorAll('.cat-secnav-areaname span, span[title]')) {
                      const title = norm(span.getAttribute('title') || '');
                      if (title.includes('selected node ' + target) || title.includes('selected ' + target))
                        return true;
                    }
                    for (const el of doc.querySelectorAll('.cat-secnav-areaname a, #cat-list a, .cat-list a')) {
                      const label = norm(el.innerText || el.textContent);
                      const title = norm(el.getAttribute('title') || '');
                      const spanTitle = norm(el.querySelector('span')?.getAttribute('title') || '');
                      if (label !== target && !title.includes(target) && !spanTitle.includes(target)) continue;
                      const cls = (el.className || '') + ' ' + (el.parentElement?.className || '');
                      if (/selected|active|current/i.test(cls)) return true;
                      if (spanTitle.includes('selected node ' + target)) return true;
                    }
                  }
                  return false;
                }
                """,
                category);
        }

        async Task<bool> ClickCategoryAsync(IPage catalogPg, string category)
        {
            foreach (var frame in catalogPg.Frames)
            {
                var link = FindCategoryLink(frame, category);
                if (await link.CountAsync() == 0)
                    continue;

                await link.First.ClickAsync(new LocatorClickOptions { Force = true });
                return true;
            }

            return await catalogPg.EvaluateAsync<bool>(
                """
                (text) => {
                  const norm = (v) => (v || '').replace(/\s+/g, ' ').trim().toLowerCase();
                  const target = norm(text);
                  const isCpo = target === 'cpo';
                  const matches = (el) => {
                    const label = norm(el.innerText || el.textContent);
                    const title = norm(el.getAttribute?.('title') || '');
                    const spanTitle = norm(el.querySelector?.('span')?.getAttribute?.('title') || '');
                    if (isCpo) return spanTitle.includes('cpo') || title.includes('cpo') || label === 'cpo';
                    return label === target || title === target || spanTitle.includes(target);
                  };
                  const click = (el) => (el.closest('a') || el.querySelector('a') || el).click();
                  const collectDocs = (win, docs = []) => {
                    try { docs.push(win.document); for (const f of win.frames) collectDocs(f, docs); } catch {}
                    return docs;
                  };
                  for (const doc of collectDocs(window)) {
                    const nodes = isCpo
                      ? doc.querySelectorAll('.cat-secnav-areaname a, .cat-secnav-areaname span, a:has(span[title*="CPO" i])')
                      : doc.querySelectorAll('.cat-secnav-areaname a, .cat-secnav-areaname span, #cat-list a, .cat-list a, a');
                    for (const el of nodes) {
                      if (!matches(el)) continue;
                      click(el);
                      return true;
                    }
                  }
                  return false;
                }
                """,
                category);
        }

        ILocator FindCategoryLink(IFrame frame, string text)
        {
            if (text.Equals("CPO", StringComparison.OrdinalIgnoreCase))
            {
                return frame.Locator(".cat-secnav-areaname a:has(span[title*='CPO' i])")
                    .Or(frame.Locator("a:has(span[title*='Unselected Node CPO' i])"))
                    .Or(frame.Locator("a:has(span[title*='Selected Node CPO' i])"))
                    .Or(frame.Locator(".cat-secnav-areaname a").Filter(new() { HasTextRegex = new Regex("^CPO$", RegexOptions.IgnoreCase) }));
            }

            var pattern = new Regex($"^{Regex.Escape(text)}$", RegexOptions.IgnoreCase);
            return frame.Locator(".cat-secnav-areaname a").Filter(new() { HasTextRegex = pattern })
                .Or(frame.Locator($".cat-secnav-areaname a:has(span[title*='{text}' i])"))
                .Or(frame.Locator($"a:has(span[title*='{text}' i])"));
        }

        async Task WaitForCategoryChangeAsync(IPage catalogPg, string category, string fingerprintBefore)
        {
            try
            {
                await catalogPg.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 15_000 });
            }
            catch (TimeoutException)
            {
                await catalogPg.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            }

            for (var attempt = 0; attempt < 30; attempt++)
            {
                await catalogPg.WaitForTimeoutAsync(500);

                var fingerprintAfter = await GetProductFingerprintAsync(catalogPg);
                if (!string.IsNullOrEmpty(fingerprintAfter) &&
                    !string.Equals(fingerprintBefore, fingerprintAfter, StringComparison.Ordinal))
                    return;

                if (await IsCategoryActiveAsync(catalogPg, category) &&
                    !string.IsNullOrEmpty(fingerprintAfter) &&
                    attempt >= 2)
                    return;

                if (!string.IsNullOrEmpty(fingerprintAfter) && attempt >= 6)
                    return;
            }

            if (await GetProductCountAsync(catalogPg) > 0)
                return;
                throw new InvalidOperationException("Catalog category page loaded but no products were visible.");
        }

        async Task WaitForProductsAsync(IPage catalogPg, int maxWaitSeconds)
        {
            for (var attempt = 0; attempt < maxWaitSeconds * 2; attempt++)
            {
                if (await GetProductCountAsync(catalogPg) > 0)
                    return;

                await catalogPg.WaitForTimeoutAsync(500);
            }
        }

        async Task<int> GetProductCountAsync(IPage catalogPg)
        {
            var total = 0;
            foreach (var frame in catalogPg.Frames)
            {
                total += await frame.Locator(".catalauge-item-holder").CountAsync();
                total += await frame.Locator("table.product-list tr.odd, table.product-list tr.even").CountAsync();
            }

            return total;
        }

        async Task<IFrame?> FindCatalogFrameAsync(IPage catalogPg)
        {
            foreach (var frame in catalogPg.Frames)
            {
                if (await frame.Locator(".catalauge-item-holder, table.product-list tr.odd, table.product-list tr.even").CountAsync() > 0)
                    return frame;
            }

            return null;
        }

        async Task<List<Dictionary<string, object?>>> ScrapeProductsAsync(IPage catalogPg, string category)
        {
            var frame = await FindCatalogFrameAsync(catalogPg);
            if (frame is null)
                return [];

            var gridCount = await frame.Locator(".catalauge-item-holder").CountAsync();
            if (gridCount > 0)
                return await ScrapeGridAsync(catalogPg, frame, gridCount, category);

            var tableCount = await frame.Locator("table.product-list tr.odd, table.product-list tr.even").CountAsync();
            if (tableCount > 0)
                return await ScrapeTableAsync(catalogPg, frame, tableCount, category);

            return [];
        }

        async Task RefreshCatalogAfterEaAsync(IPage catalogPg, string category)
        {
            await catalogPg.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await OpenCategoryAfterReloadAsync(catalogPg, category);
            await catalogPg.WaitForTimeoutAsync(2500);
        }

        async Task OpenCategoryAfterReloadAsync(IPage catalogPg, string category)
        {
            await WaitForProductsAsync(catalogPg, 8);
            await NavigateToCategoryAsync(catalogPg, category);
        }

        async Task EnsureAllEaSelectedAsync(IPage catalogPg, IFrame frame, int count, bool isGrid)
        {
            for (var index = 0; index < count; index++)
            {
                if (isGrid)
                {
                    var items = frame.Locator(".catalauge-item-holder");
                    if (index >= await items.CountAsync())
                        break;

                    var item = items.Nth(index);
                    if (!await IsEaSelectedAsync(item))
                        await SelectEaAsync(catalogPg, item);
                }
                else
                {
                    var tableRows = frame.Locator("table.product-list tr.odd, table.product-list tr.even");
                    if (index >= await tableRows.CountAsync())
                        break;

                    var row = tableRows.Nth(index);
                    if (!await IsEaSelectedAsync(row))
                        await SelectEaAsync(catalogPg, row);
                }
            }
        }

        async Task<List<Dictionary<string, object?>>> ScrapeGridAsync(IPage catalogPg, IFrame frame, int itemCount, string category)
        {
            await EnsureAllEaSelectedAsync(catalogPg, frame, itemCount, isGrid: true);

            if (category.Equals("Phones", StringComparison.OrdinalIgnoreCase))
                await RefreshCatalogAfterEaAsync(catalogPg, category);
            else
                await catalogPg.WaitForTimeoutAsync(1500);

            frame = await FindCatalogFrameAsync(catalogPg);
            if (frame is null)
                return [];

            var itemsAfter = frame.Locator(".catalauge-item-holder");
            var count = await itemsAfter.CountAsync();

            await EnsureAllEaSelectedAsync(catalogPg, frame, count, isGrid: true);
            await catalogPg.WaitForTimeoutAsync(2000);

            var rows = new List<Dictionary<string, object?>>(count);

            for (var index = 0; index < count; index++)
            {
                var item = itemsAfter.Nth(index);
                if (!await IsEaSelectedAsync(item))
                {
                    await SelectEaAsync(catalogPg, item);
                    await catalogPg.WaitForTimeoutAsync(350);
                }

                var (name, sku) = await ReadGridMetaAsync(item);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                rows.Add(CreateExportRow(name, sku, await ReadPriceAsync(item)));
            }

            return rows;
        }

        async Task<List<Dictionary<string, object?>>> ScrapeTableAsync(IPage catalogPg, IFrame frame, int rowCount, string category)
        {
            await EnsureAllEaSelectedAsync(catalogPg, frame, rowCount, isGrid: false);

            if (category.Equals("Phones", StringComparison.OrdinalIgnoreCase))
                await RefreshCatalogAfterEaAsync(catalogPg, category);
            else
                await catalogPg.WaitForTimeoutAsync(1500);

            frame = await FindCatalogFrameAsync(catalogPg);
            if (frame is null)
                return [];

            var tableRowsAfter = frame.Locator("table.product-list tr.odd, table.product-list tr.even");
            var count = await tableRowsAfter.CountAsync();

            await EnsureAllEaSelectedAsync(catalogPg, frame, count, isGrid: false);
            await catalogPg.WaitForTimeoutAsync(2000);

            var rows = new List<Dictionary<string, object?>>(count);

            for (var index = 0; index < count; index++)
            {
                var row = tableRowsAfter.Nth(index);
                if (!await IsEaSelectedAsync(row))
                {
                    await SelectEaAsync(catalogPg, row);
                    await catalogPg.WaitForTimeoutAsync(350);
                }

                var productLink = row.Locator("td.product a").First;
                var name = await productLink.CountAsync() > 0
                    ? (await productLink.InnerTextAsync()).Trim()
                    : (await row.Locator("td.product").First.InnerTextAsync()).Trim();

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var sku = await ReadTextAsync(row, "td.item", "td.prod-number");
                rows.Add(CreateExportRow(name, sku, await ReadPriceAsync(row)));
            }

            return rows;
        }

        async Task<bool> IsEaSelectedAsync(ILocator row) =>
            await row.EvaluateAsync<bool>(
                """
                (el) => {
                  const sel = el.querySelector('.catalauge-item-links-table select, select');
                  if (!sel) return false;
                  return /\bEA\b/i.test(sel.options[sel.selectedIndex]?.textContent || '');
                }
                """);

        async Task SelectEaAsync(IPage catalogPg, ILocator row)
        {
            if (await IsEaSelectedAsync(row))
                return;

            var unitSelect = row.Locator(".catalauge-item-links-table select, select").First;
            if (await unitSelect.CountAsync() == 0)
            {
                var eaChoice = row.GetByText(new Regex("^EA$", RegexOptions.IgnoreCase)).First;
                if (await eaChoice.CountAsync() > 0)
                    await eaChoice.ClickAsync(new LocatorClickOptions { Force = true });

                return;
            }

            var eaOption = unitSelect.Locator("option").Filter(new() { HasTextRegex = new Regex("\\bEA\\b", RegexOptions.IgnoreCase) });
            if (await eaOption.CountAsync() == 0)
                return;

            var value = await eaOption.First.GetAttributeAsync("value") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(value))
                await unitSelect.SelectOptionAsync(value, new LocatorSelectOptionOptions { Force = true });
            else
                await unitSelect.SelectOptionAsync(new SelectOptionValue { Label = "EA" }, new LocatorSelectOptionOptions { Force = true });
        }

        async Task<(string Name, string Sku)> ReadGridMetaAsync(ILocator item)
        {
            var name = await item.EvaluateAsync<string>(
                """
                (el) => {
                  const title = el.querySelector('.catalauge-item-title a, .catalauge-item-title');
                  const lines = (el.innerText || '').split(/\n+/).map(s => s.trim()).filter(Boolean);
                  let name = (title?.innerText || '').trim();
                  if (!name) name = lines.find(l => !/^\d{8,}$/.test(l)) || '';
                  return name;
                }
                """);

            var sku = await item.EvaluateAsync<string>(
                """
                (el) => {
                  const lines = (el.innerText || '').split(/\n+/).map(s => s.trim()).filter(Boolean);
                  return lines.find(l => /^\d{8,}$/.test(l)) || '';
                }
                """);

            return NormalizeFields(name ?? string.Empty, sku ?? string.Empty);
        }

        async Task<string> ReadPriceAsync(ILocator scope)
        {
            var priceText = await scope.EvaluateAsync<string>(
                """
                (el) => {
                  const priceEl = el.querySelector('.catalauge-item-price');
                  const direct = (priceEl?.innerText || priceEl?.textContent || '').trim();
                  if (direct) return direct;
                  const text = el.innerText || '';
                  const usd = text.match(/([\d,]+\.\d{2})\s*USD/i);
                  if (usd) return usd[1];
                  const labeled = text.match(/Price:\s*\$?\s*([\d,]+\.\d{2})/i);
                  if (labeled) return labeled[1];
                  const generic = text.replace(/\s+/g, ' ').match(/\$\s*[\d,]+\.\d{2}|\b[\d,]+\.\d{2}\b/);
                  return generic ? generic[0].replace('$', '').trim() : '';
                }
                """);

            if (ParsePrice(priceText) is not null)
                return priceText ?? string.Empty;

            foreach (var selector in new[] { "td.price td.b2b-prd-prc", "td.b2b-prd-prc", "td.price", ".catalauge-item-price" })
            {
                var target = scope.Locator(selector);
                for (var index = 0; index < await target.CountAsync(); index++)
                {
                    var text = (await target.Nth(index).InnerTextAsync()).Trim();
                    if (ParsePrice(text) is not null)
                        return text;
                }
            }

            return string.Empty;
        }

        async Task<string> ReadTextAsync(ILocator scope, params string[] selectors)
        {
            foreach (var selector in selectors)
            {
                var target = scope.Locator(selector).First;
                if (await target.CountAsync() == 0)
                    continue;

                var text = (await target.InnerTextAsync()).Trim();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }

            return string.Empty;
        }

        (string Name, string Sku) NormalizeFields(string productName, string sku)
        {
            productName = Regex.Replace(productName.Trim(), @"[\r\n]+", " ").Trim();
            sku = sku.Trim();

            if (materialNumberPattern.IsMatch(sku))
            {
                productName = Regex.Replace(productName.Replace(sku, string.Empty, StringComparison.Ordinal), @"\s{2,}", " ").Trim();
                return (productName, sku);
            }

            var lines = productName.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length >= 2 && materialNumberPattern.IsMatch(lines[0]))
                return (string.Join(" ", lines.Skip(1)), lines[0]);

            var inline = Regex.Match(productName, @"^(\d{8,})\s+(.+)$");
            if (inline.Success)
                return (inline.Groups[2].Value.Trim(), inline.Groups[1].Value);

            return (productName, sku);
        }

        Dictionary<string, object?> CreateExportRow(string productName, string sku, string priceText)
        {
            var (name, normalizedSku) = NormalizeFields(productName, sku);
            return new Dictionary<string, object?>
            {
                ["Product Name"] = name,
                ["SKU"] = normalizedSku,
                ["Price"] = ParsePrice(priceText)
            };
        }

        decimal? ParsePrice(string? priceText)
        {
            if (string.IsNullOrWhiteSpace(priceText))
                return null;

            var match = pricePattern.Match(priceText);
            var normalized = (match.Success ? match.Groups[1].Value : priceText).Replace("$", "").Replace(",", "").Trim();
            return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var price) ? price : null;
        }

        async Task SaveDebugSnapshotAsync(IPage snapshotPage, string label)
        {
            try
            {
                var outDir = Path.Combine(AppContext.BaseDirectory, "App_Data", "scrape-debug");
                Directory.CreateDirectory(outDir);
                var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var basePath = Path.Combine(outDir, $"{stamp}-{label}");
                await snapshotPage.ScreenshotAsync(new PageScreenshotOptions { Path = $"{basePath}.png", FullPage = true });
                await File.WriteAllTextAsync($"{basePath}.html", await snapshotPage.ContentAsync());
            }
            catch
            {
                // ignore debug IO errors
            }
        }
    }

    private static async Task<(IPlaywright Playwright, IBrowser Browser, IPage Page)> InitializeBrowserAsync(ScrapingOptions options, CancellationToken cancellationToken)
    {
        if (!_browsersReady)
        {
            await InstallLock.WaitAsync(cancellationToken);
            try
            {
                if (!_browsersReady)
                {
                    var browsersPath = Path.Combine(AppContext.BaseDirectory, "playwright-browsers");
                    Directory.CreateDirectory(browsersPath);
                    Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH", browsersPath);

                    if (!await CanLaunchChromiumAsync())
                    {
                        var exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
                        if (exitCode != 0)
                            throw new InvalidOperationException("Playwright browser install failed. Ensure the server has internet access on first run.");

                        if (!await CanLaunchChromiumAsync())
                            throw new InvalidOperationException("Playwright Chromium installed but could not be launched.");
                    }

                    _browsersReady = true;
                }
            }
            finally
            {
                InstallLock.Release();
            }
        }

        var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = options.Headless,
            SlowMo = options.SlowMo > 0 ? options.SlowMo : null,
            Args = ["--start-maximized", "--disable-blink-features=AutomationControlled"]
        });

        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
        });

        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(options.TimeoutSeconds * 1_000);

        return (playwright, browser, page);

        static async Task<bool> CanLaunchChromiumAsync()
        {
            try
            {
                using var pw = await Playwright.CreateAsync();
                await using var testBrowser = await pw.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
                return true;
            }
            catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
    }
}
