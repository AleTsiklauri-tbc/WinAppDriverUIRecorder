# Target style: LMSAutomationDemo

Real files from the framework that generated code must fit into. Spec section 15 says these files win over
any written description. Sanitized: loan ids and database names were replaced.

| File | Layer | Source in LMSAutomationDemo |
|---|---|---|
| [ClientSearchPage.java](ClientSearchPage.java) | Page: locators only | `src/main/java/tbc/automation/pages/Landing/clientCenter/clientSearch/` |
| [ClientSearchSteps.java](ClientSearchSteps.java) | Steps: fluent actions | `src/main/java/tbc/automation/steps/Landing/clientCenter/clientSearch/` |
| [ChangeOfTermsTest.java](ChangeOfTermsTest.java) | Test: TestNG | `src/test/java/tbc/automation/tests/loanUpdateOperations/` |

## Project profile (spec section 15)

Every item was read from the code and confirmed when the analysis recommendations were accepted.

| Item | Value |
|---|---|
| Java version and build tool | Java 17 (`maven.compiler.source/target`), Maven. Suites run from `testng*.xml` with `-DgroupName` |
| Appium java-client | **7.6.0** (brings Selenium 3.141.59). Locators use `winDriver.findElementByAccessibilityId/ByName/ByXPath/ByClassName`. `AppiumBy` does not exist in this version |
| Driver type | Raw `io.appium.java_client.windows.WindowsDriver` (no generics), created by `tbc.automation.config.DriverManager`, 5 s implicit wait |
| Test framework | TestNG 7.11.0, `org.testng.Assert` |
| Base page class | `tbc.automation.pages.BasePage(WebDriver)` with `waitForElementTobeVisible(WebElement, int)` and `waitForEnabled(WebElement, int)`. No click/type helpers |
| How locators are declared | `public WebElement getXxx(){return winDriver.findElementByAccessibilityId("id");}`, one line or three. Each page keeps `private (final) WindowsDriver winDriver` |
| Page method style | **Three layers.**<br>• Pages hold locators only.<br>• Steps classes (`XxxSteps`, constructed with `WindowsDriver`) hold fluent actions that `return this;` and log each action with `log.info(...)` (`tbc.automation.utils.Log`).<br>• Tests chain steps |
| Windows and dialogs | Owned dialogs are reached from the main session. After login: `new Utility().printAndSwitchToAllWindows(winDriver)`. Also `Utility.switchToWindowByTitle(driver, title)`. WebView2 content goes through `BaseSteps.switchToChromeWidget()` |
| Test data | Literals in tests, constants in `tbc.automation.data.*`, DB lookups (`LmsQueries`, `B7Queries`), `ConfigReader.getProperty(KEY)` (env var first, then `config.properties`). Secrets come from `ConfigReader.getProperty("<KEY>")` |
| Packages | Pages: `tbc.automation.pages.<Area>[.<sub>]`. Steps mirror them: `tbc.automation.steps.<Area>[.<sub>]`. Tests: `tbc.automation.tests.<feature>` under `src/test/java` |
| Naming | Classes `<Name>Page`, `<Name>Steps`, `<Name>Test`. Getters `get<Purpose><Suffix>()`; suffixes in use: Button 89, EditBox 17, Tab 13, Dropdown 12, Field 8, Dialog 6, Input 6, Checkbox 5, TextBox 5. Step verbs: `click…`, `input…`/`enter…`/`set…`, `select…`, `moveTo…Tab`, `check…Checkbox` |
| Test class shape | `@ZephyrScenario("SQDLA-T…")` plus `@Test(description = "...", groups = {"…", "Regression"})` on the class, which extends `tbc.automation.BaseTest`. Ordered `@Test(priority = n)` methods share state through fields |
| Combo selection | `getXxxDropdown().sendKeys(text)` then `sendKeys(Keys.ENTER)`, rather than clicking list items |
| Grids | `GridSteps.clickCell(rowIndex, headerName)` on the grid with AutomationId `Grid`; rows are 0-based |
